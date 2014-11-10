﻿// Copyright (c) 2014, Pierre Chalamet
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
//     * Redistributions of source code must retain the above copyright
//       notice, this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above copyright
//       notice, this list of conditions and the following disclaimer in the
//       documentation and/or other materials provided with the distribution.
//     * Neither the name of Pierre Chalamet nor the
//       names of its contributors may be used to endorse or promote products
//       derived from this software without specific prior written permission.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
// ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL PIERRE CHALAMET BE LIABLE FOR ANY
// DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
// (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
// ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
// SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FullBuild.Config;
using FullBuild.Helpers;
using FullBuild.Model;
using FullBuild.SourceControl;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace FullBuild.Commands
{
    internal class Workspace
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public void Init(string path)
        {
            var wsDir = new DirectoryInfo(path);
            wsDir.Create();

            var admDir = wsDir.GetDirectory(".full-build");
            if (admDir.Exists)
            {
                throw new ArgumentException("Workspace is already initialized");
            }

            // get bootstrap config
            var config = ConfigManager.GetConfig(wsDir);

            var sourceControl = ServiceActivator<Factory>.Create<ISourceControl>(config.SourceControl);
            Console.WriteLine("Cloning administrative repo");
            sourceControl.Clone(admDir, "full-build", config.AdminRepo);

            // reload config now
            config = ConfigManager.GetConfig(wsDir);
            
            // copy all files from binary repo
            var tip = sourceControl.Tip(admDir);
            var binDir = new DirectoryInfo(config.BinRepo);
            var binVersionDir = binDir.GetDirectory(tip);
            if (binVersionDir.Exists)
            {
                Console.WriteLine("Copying build output version {0}", tip);
                var targetBinDir = wsDir.GetDirectory("bin");
                targetBinDir.Create();
                foreach (var binFile in binVersionDir.EnumerateFiles())
                {
                    var targetFile = targetBinDir.GetFile(binFile.Name);
                    binFile.CopyTo(targetFile.FullName, true);
                }
            }
        }

        public void Update()
        {
            var workspace = WellKnownFolders.GetWorkspaceDirectory();
            var config = ConfigManager.GetConfig(workspace);

            // get all csproj in all repos only
            var anthology = LoadOrCreateAnthology();
            anthology = UpdateAnthologyFromSource(config, workspace, anthology);

            // merge with existing
            anthology = MergeNewAnthologyWithExisting(anthology);

            // get packages
            var handler = new Packages();
            handler.Install();

            // Promotion
            anthology = OptimizeAnthology(anthology);

            // merge with existing
            anthology = MergeNewAnthologyWithExisting(anthology);

            // Generate import files
            GenerateImports(anthology);
        }

        public void CloneRepo(string[] repos)
        {
            var wsDir = WellKnownFolders.GetWorkspaceDirectory();
            var config = ConfigManager.GetConfig(wsDir);

            // validate first that repos are valid and clone them
            var sourceControl = ServiceActivator<Factory>.Create<ISourceControl>(config.SourceControl);
            foreach (var repo in repos)
            {
                var match = "^" + repo + "$";
                var regex = new Regex(match, RegexOptions.IgnoreCase);
                var repoConfigs = config.SourceRepos.Where(x => regex.IsMatch(x.Name));
                if (!repoConfigs.Any())
                {
                    throw new ArgumentException("Invalid repo " + repo);
                }

                foreach (var repoConfig in repoConfigs)
                {
                    var repoDir = wsDir.GetDirectory(repoConfig.Name);
                    if (!repoDir.Exists)
                    {
                        Console.WriteLine("Cloning repo {0}", repoConfig.Name, repo);
                        sourceControl.Clone(repoDir, repoConfig.Name, repoConfig.Url);
                    }
                }
            }
        }

        private static Anthology OptimizeAnthology(Anthology anthology)
        {
            Console.WriteLine("Optimizing anthology");
            anthology = RemoveBinariesFromPackages(anthology);
            anthology = PromoteBinaryToProject(anthology);
            anthology = PromotePackageToProject(anthology);
            anthology = RemoveUnusedStuff(anthology);
            return anthology;
        }

        private static Anthology RemoveUnusedStuff(Anthology anthology)
        {
            // remove empty packages (no assembly)
            anthology = RemoveEmptyPackages(anthology);

            // remove unused package
            var usedPackages = anthology.Projects.SelectMany(x => x.PackageReferences).Distinct();

            var packagesToRemove = from package in anthology.Packages
                                   where !usedPackages.Contains(package.Name, StringComparer.InvariantCultureIgnoreCase)
                                   select package;

            anthology = packagesToRemove.Aggregate(anthology, (a, p) => a.RemovePackage(p));

            // remove unused binary
            var usedBinaries = anthology.Projects.SelectMany(x => x.BinaryReferences).Distinct();

            var binariesToRemove = from binary in anthology.Binaries
                                   where !usedBinaries.Contains(binary.AssemblyName, StringComparer.InvariantCultureIgnoreCase)
                                   select binary;

            anthology = binariesToRemove.Aggregate(anthology, (a, b) => a.RemoveBinary(b));
            return anthology;
        }

        private static Anthology RemoveEmptyPackages(Anthology anthology)
        {
            var pkgsDir = WellKnownFolders.GetPackageDirectory();

            var emptyPackages = from package in anthology.Packages
                                let pkgdir = pkgsDir.GetDirectory(package.Name)
                                where pkgdir.Exists
                                let assemblies = Nuspec.Assemblies(pkgdir)
                                where !assemblies.Any()
                                select package;

            foreach(var project in anthology.Projects)
            {
                var newProject = emptyPackages.Aggregate(project, (p, pa) => p.RemovePackageReference(pa.Name));
                anthology = anthology.AddOrUpdateProject(newProject);
            }

            anthology = emptyPackages.Aggregate(anthology, (a, p) => a.RemovePackage(p));
            return anthology;
        }

        private static Anthology PromoteBinaryToProject(Anthology anthology)
        {
            var bin2prj = from binary in anthology.Binaries
                from project in anthology.Projects
                where binary.AssemblyName.InvariantEquals(project.AssemblyName)
                let newBin2Prj = new {Bin = binary, Prj = project}
                group newBin2Prj by newBin2Prj.Bin
                into g
                select g;

            foreach (var b2p in bin2prj)
            {
                var bin = b2p.Key;
                if (1 < b2p.Count())
                {
                    Console.WriteLine("WARNING: Too many candidate projects to promote binary {0} to project:", bin.AssemblyName);
                    b2p.ForEach(x => Console.WriteLine("  {0} {1}", Path.GetFileName(x.Prj.ProjectFile), x.Prj.Guid.ToString("B")));
                }
                else
                {
                    var prj = b2p.Single().Prj;
                    _logger.Debug("Converting binary {0} to project {1}", bin.AssemblyName, prj.ProjectFile);
                    foreach (var project in anthology.Projects)
                    {
                        if (project.BinaryReferences.Contains(bin.AssemblyName, StringComparer.InvariantCultureIgnoreCase))
                        {
                            var newProject = project.RemoveBinaryReference(bin.AssemblyName);
                            newProject = newProject.AddProjectReference(prj.Guid);
                            anthology = anthology.AddOrUpdateProject(newProject);
                        }
                    }
                }
            }

            return anthology;
        }

        private static Anthology PromotePackageToProject(Anthology anthology)
        {
            var pkgsDir = WellKnownFolders.GetPackageDirectory();

            var pkg2prj = from package in anthology.Packages
                let pkgdir = pkgsDir.GetDirectory(package.Name)
                where pkgdir.Exists
                let assemblies = Nuspec.Assemblies(pkgdir)
                from project in anthology.Projects
                where assemblies.Contains(project.AssemblyName, StringComparer.InvariantCultureIgnoreCase)
                let newPkg2prj = new {Pkg = package, Prj = project}
                group newPkg2prj by newPkg2prj.Pkg into g
                select g;

            foreach(var p2p in pkg2prj)
            {
                var pkg = p2p.Key;
                if (1 < p2p.Count())
                {
                    Console.WriteLine("WARNING: Too many candidate projects to promote package {0} to project:", pkg.Name);
                    p2p.ForEach(x => Console.WriteLine("  {0} {1}", Path.GetFileName(x.Prj.ProjectFile), x.Prj.Guid.ToString("B")));
                }
                else
                {
                    var prj = p2p.Single().Prj;
                    _logger.Debug("Converting package {0} to project {1}", pkg.Name, prj.ProjectFile);
                    foreach(var project in anthology.Projects)
                    {
                        if (project.PackageReferences.Contains(pkg.Name, StringComparer.InvariantCultureIgnoreCase))
                        {
                            var newProject = project.RemovePackageReference(pkg.Name);
                            newProject = newProject.AddProjectReference(prj.Guid);
                            anthology = anthology.AddOrUpdateProject(newProject);
                        }
                    }
                }
            }
            return anthology;
        }

        private static Anthology RemoveBinariesFromPackages(Anthology anthology)
        {
            var pkgsDir = WellKnownFolders.GetPackageDirectory();
            foreach(var project in anthology.Projects)
            {
                // gather all assemblies from packages in this project
                var importedAssemblies = (from pkgRef in project.PackageReferences
                                          let pkgdir = pkgsDir.GetDirectory(pkgRef)
                                          where pkgdir.Exists
                                          select Nuspec.Assemblies(pkgdir)).SelectMany(x => x).Distinct(StringComparer.InvariantCultureIgnoreCase);

                // remove imported assemblies
                var newProject = importedAssemblies.Aggregate(project, (p, a) => p.RemoveBinaryReference(a));
                anthology = anthology.AddOrUpdateProject(newProject);
            }
            return anthology;
        }

        private void GenerateImports(Anthology anthology)
        {
            var targetDir = WellKnownFolders.GetProjectDirectory();
            targetDir.Create();

            foreach(var project in anthology.Projects)
            {
                var projectFile = Path.Combine(WellKnownFolders.MsBuildSolutionDir, project.ProjectFile);
                var binFile = Path.Combine(WellKnownFolders.MsBuildBinDir, project.AssemblyName + project.Extension);
                var projectProperty = ((project.AssemblyName + project.FxTarget).Replace('.', '_') + "_Src").Replace('.', '_');
                var srcCondition = string.Format("'$({0}_Src)' != ''", projectProperty);
                var binCondition = string.Format("'$({0}_Src)' == ''", projectProperty);

                var xdoc = new XElement(XmlHelpers.NsMsBuild + "Project",
                                        new XElement(XmlHelpers.NsMsBuild + "Import",
                                                     new XAttribute("Project", Path.Combine(WellKnownFolders.MsBuildViewDir, "$(SolutionName).targets")),
                                                     new XAttribute("Condition", "'$(BinSrcConfig)' == ''")),
                                        new XElement(XmlHelpers.NsMsBuild + "ItemGroup",
                                                     new XElement(XmlHelpers.NsMsBuild + "ProjectReference",
                                                                  new XAttribute("Include", projectFile),
                                                                  new XAttribute("Condition", srcCondition),
                                                                  new XElement(XmlHelpers.NsMsBuild + "Project", project.Guid.ToString("B")),
                                                                  new XElement(XmlHelpers.NsMsBuild + "Name", project.AssemblyName)),
                                                     new XElement(XmlHelpers.NsMsBuild + "Reference",
                                                                  new XAttribute("Include", project.AssemblyName),
                                                                  new XAttribute("Condition", binCondition),
                                                                  new XElement(XmlHelpers.NsMsBuild + "HintPath", binFile),
                                                                  new XElement(XmlHelpers.NsMsBuild + "Private", "true"))));

                var targetFileName = project.Guid + ".targets";
                var prjImport = targetDir.GetFile(targetFileName);
                xdoc.Save(prjImport.FullName);
            }
        }

        private static Anthology UpdateAnthologyFromSource(FullBuildConfig config, DirectoryInfo workspace, Anthology anthology)
        {
            foreach(var repo in config.SourceRepos)
            {
                Console.WriteLine("Processing repo {0}:", repo.Name);
                var repoDir = workspace.GetDirectory(repo.Name);
                if (! repoDir.Exists)
                {
                    continue;
                }

                // delete all solution files
                var slns = repoDir.EnumerateFiles("*.sln", SearchOption.AllDirectories);
                slns.ForEach(x => x.Delete());

                // process all projects
                var csprojs = repoDir.EnumerateFiles("*.csproj", SearchOption.AllDirectories);
                anthology = csprojs.Aggregate(anthology, (a, p) => ParseAndAddProject(workspace, p, a));
            }

            return anthology;
        }

        private static Anthology LoadOrCreateAnthology()
        {
            var admDir = WellKnownFolders.GetAdminDirectory();
            var anthologyFile = admDir.GetFile(Anthology.AnthologyFileName);
            if (anthologyFile.Exists)
            {
                var oldJson = File.ReadAllText(anthologyFile.FullName);
                var prevAnthology = JsonConvert.DeserializeObject<Anthology>(oldJson);
                var oldJ = JObject.FromObject(prevAnthology);
                var anthology = oldJ.ToObject<Anthology>();
                return anthology;
            }

            return new Anthology();
        }

        private static Anthology MergeNewAnthologyWithExisting(Anthology anthology)
        {
            // merge anthology files
            var admDir = WellKnownFolders.GetAdminDirectory();
            var anthologyFile = admDir.GetFile(Anthology.AnthologyFileName);
            if (anthologyFile.Exists)
            {
                var oldJson = File.ReadAllText(anthologyFile.FullName);
                var prevAnthology = JsonConvert.DeserializeObject<Anthology>(oldJson);
                var newJ = JObject.FromObject(anthology);
                var oldJ = JObject.FromObject(prevAnthology);

                var mergeSettings = new JsonMergeSettings {MergeArrayHandling = MergeArrayHandling.Replace};
                oldJ.Merge(newJ, mergeSettings);
                anthology = oldJ.ToObject<Anthology>();
            }

            var json = JsonConvert.SerializeObject(anthology, Formatting.Indented);
            File.WriteAllText(anthologyFile.FullName, json);

            return anthology;
        }

        private static IEnumerable<Model.Package> GetNugetPackages(DirectoryInfo projectDir)
        {
            var packageFile = projectDir.GetFile("packages.config");
            if (!packageFile.Exists)
            {
                return Enumerable.Empty<Model.Package>();
            }

            var docPackage = XDocument.Load(packageFile.FullName);
            var packages = from element in docPackage.Descendants("package")
                           let name = (string) element.Attribute("id")
                           let version = (string) element.Attribute("version")
                           select new Model.Package(name, version);

            return packages;
        }

        private static Anthology ParseAndAddProject(DirectoryInfo workspace, FileInfo projectFile, Anthology anthology)
        {
            Console.WriteLine("  Found project {0}", projectFile.FullName);

            var xdoc = XDocument.Load(projectFile.FullName);

            // extract infos from project
            var projectFileName = projectFile.FullName.Substring(workspace.FullName.Length + 1);
            Guid projectGuid;
            try
            {
                projectGuid = Guid.ParseExact((string) xdoc.Descendants(XmlHelpers.NsMsBuild + "ProjectGuid").Single(), "B");
            }
            catch (Exception)
            {
                projectGuid = Guid.ParseExact((string)xdoc.Descendants(XmlHelpers.NsMsBuild + "ProjectGuid").Single(), "D");
            }

            var assemblyName = (string) xdoc.Descendants(XmlHelpers.NsMsBuild + "AssemblyName").Single();
            var fxTarget = (string) xdoc.Descendants(XmlHelpers.NsMsBuild + "TargetFrameworkVersion").SingleOrDefault() ?? "v4.5";
            var extension = ((string) xdoc.Descendants(XmlHelpers.NsMsBuild + "OutputType").Single()).InvariantEquals("Library") ? ".dll" : ".exe";

            // extract project references
            var projectReferences = from prjRef in xdoc.Descendants(XmlHelpers.NsMsBuild + "ProjectReference").Descendants(XmlHelpers.NsMsBuild + "Project")
                                    select Guid.ParseExact(prjRef.Value, "B");
            var fbProjectReferences = from import in xdoc.Descendants(XmlHelpers.NsMsBuild + "Import")
                                      let importProject = (string) import.Attribute("Project")
                                      where importProject.InvariantStartsWith(WellKnownFolders.MsBuildProjectDir)
                                      let importProjectName = Path.GetFileNameWithoutExtension(importProject)
                                      select Guid.Parse(importProjectName);
            var allProjectReferences = projectReferences.Concat(fbProjectReferences).ToImmutableList();

            // extract binary references - both nuget and direct reference to assemblies (broken project reference)
            var binaries = from binRef in xdoc.Descendants(XmlHelpers.NsMsBuild + "Reference")
                           let assName = new AssemblyName((string) binRef.Attribute("Include")).Name
                           let maybeHintPath = binRef.Descendants(XmlHelpers.NsMsBuild + "HintPath").SingleOrDefault()
                           select new Binary(assName, null != maybeHintPath ? maybeHintPath.Value.ToUnixSeparator() : null);
            var binaryReferences = binaries.Select(x => x.AssemblyName).ToImmutableList();

            // extract all packages
            var nugetPackages = GetNugetPackages(projectFile.Directory);
            var fbPackages = from import in xdoc.Descendants(XmlHelpers.NsMsBuild + "Import")
                             let importProject = (string) import.Attribute("Project")
                             where importProject.InvariantStartsWith(WellKnownFolders.MsBuildPackagesDir)
                             let importProjectName = Path.GetFileNameWithoutExtension(importProject)
                             select new Model.Package(importProjectName, "0.0.0");
            var packages = nugetPackages.Concat(fbPackages);
            var packageNames = packages.Select(x => x.Name).ToImmutableList();

            // update anthology with this new project
            var project = new Model.Project(projectGuid, projectFileName, assemblyName, extension, fxTarget, allProjectReferences, binaryReferences, packageNames);
            anthology = anthology.AddOrUpdateProject(project);
            anthology = binaries.Aggregate(anthology, (a, b) => a.AddOrUpdateBinary(b));
            anthology = packages.Aggregate(anthology, (a, p) => a.AddOrUpdatePackages(p));

            return anthology;
        }
    }
}