﻿//   Copyright 2014-2016 Pierre Chalamet
//
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.

module MsBuildConversion
open System.IO
open System.Xml.Linq
open System
open System.Linq
open IoHelpers
open MsBuildHelpers
open Env
open Anthology
open Collections


let private generatePackageCopy (packageRef : PackageId) =
    let propName = PackagePropertyName packageRef
    let condition = sprintf "'$(%sCopy)' == ''" propName
    let project = sprintf "$(FBWorkspaceDir)/.full-build/packages/%s/package-copy.targets" packageRef.toString
    let import = XElement(NsMsBuild + "Import",
                       XAttribute(NsNone + "Project", project),
                       XAttribute(NsNone + "Condition", condition))
    import


let private generateProjectCopy (projectRef : ProjectId) =
    let propName = ProjectPropertyName projectRef
    let condition = sprintf "'$(%sCopy)' == ''" propName
    let project = sprintf "$(FBWorkspaceDir)/.full-build/projects/%s-copy.targets" projectRef.toString
    let import = XElement(NsMsBuild + "Import",
                       XAttribute(NsNone + "Project", project),
                       XAttribute(NsNone + "Condition", condition))
    import


let private generateProjectTarget (project : Project) =
    let projectProperty = ProjectPropertyName project.ProjectId
    let srcCondition = sprintf "'$(%s)' != ''" projectProperty
    let binCondition = sprintf "'$(%s)' == ''" projectProperty
    let cpyCondition = sprintf "%s And '$(%sCopy)' == ''" binCondition projectProperty
    let projectFile = sprintf "%s/%s/%s" MSBUILD_SOLUTION_DIR (project.Repository.toString) project.RelativeProjectFile.toString
    let output = (project.Output.toString)
    let ext = match project.OutputType with
              | OutputType.Dll -> "dll"
              | OutputType.Exe -> "exe"
    let binFile = sprintf "%s/%s.%s" MSBUILD_BIN_FOLDER output ext
    let refFile = sprintf "%s/.full-build/projects/%s-copy.targets" MSBUILD_SOLUTION_DIR project.ProjectId.toString

    // This is the import targets that will be Import'ed inside a proj file.
    // First we include full-build view configuration (this is done to avoid adding an extra import inside proj)
    // Then we end up either importing output assembly or project depending on view configuration
    XDocument (
        XElement(NsMsBuild + "Project", 
            XElement (NsMsBuild + "Import",
                XAttribute (NsNone + "Project", "$(FBWorkspaceDir)/.full-build/views/$(SolutionName).targets"),
                XAttribute (NsNone + "Condition", "'$(FullBuild_Config)' == ''")),
            XElement (NsMsBuild + "ItemGroup",
                XElement(NsMsBuild + "ProjectReference",
                    XAttribute (NsNone + "Include", projectFile),
                    XAttribute (NsNone + "Condition", srcCondition),
                    XElement (NsMsBuild + "Project", sprintf "{%s}" project.UniqueProjectId.toString),
                    XElement (NsMsBuild + "Name", project.Output.toString)),
                XElement (NsMsBuild + "Reference",
                    XAttribute (NsNone + "Include", binFile),
                    XAttribute (NsNone + "Condition", binCondition),
                    XElement (NsMsBuild + "Private", "true"))),
                XElement(NsMsBuild + "Import", 
                    XAttribute(NsNone + "Project", refFile),
                    XAttribute(NsNone + "Condition", cpyCondition))))

let private generateProjectCopyTarget (project : Project) =
    let projectProperty = ProjectPropertyName project.ProjectId
    let projectCopyProperty = projectProperty + "Copy"
    let binCondition = sprintf "'$(%s)' == ''" projectProperty
    let copyCondition = sprintf "'$(%s)' == ''" projectCopyProperty
    let prjFiles = project.ProjectReferences |> Seq.map generateProjectCopy
    let pkgFiles = project.PackageReferences |> Seq.map generatePackageCopy

    let output = (project.Output.toString)
    let ext = match project.OutputType with
                | OutputType.Dll -> "dll"
                | OutputType.Exe -> "exe"
    let binFile = sprintf "%s/%s.%s" MSBUILD_BIN_FOLDER output ext
    let pdbFile = sprintf "%s/%s.pdb" MSBUILD_BIN_FOLDER output
    let incFile = sprintf "%s;%s" binFile pdbFile

    // This is the import targets that will be Import'ed inside a proj file.
    // First we include full-build view configuration (this is done to avoid adding an extra import inside proj)
    // Then we end up either importing output assembly or project depending on view configuration
    XDocument (
        XElement(NsMsBuild + "Project", 
                XAttribute (NsNone + "Condition", copyCondition),
                XElement(NsMsBuild + "PropertyGroup",
                    XElement(NsMsBuild + projectCopyProperty, "Y")),
                XElement (NsMsBuild + "ItemGroup", 
                    XElement(NsMsBuild + "FBCopyFiles", 
                        XAttribute(NsNone + "Include", incFile))),
                prjFiles,
                pkgFiles))



let private cleanupProject (xproj : XDocument) (project : Project) : XDocument =
    let filterFullBuildProject (xel : XElement) =
        let attr = !> (xel.Attribute (NsNone + "Project")) : string
        attr.StartsWith(MSBUILD_PROJECT_FOLDER, StringComparison.CurrentCultureIgnoreCase)
            || attr.StartsWith(MSBUILD_PROJECT_FOLDER2, StringComparison.CurrentCultureIgnoreCase)

    let filterFullBuildPackage (xel : XElement) =
        let attr = !> (xel.Attribute (NsNone + "Project")) : string
        attr.StartsWith(MSBUILD_PACKAGE_FOLDER, StringComparison.CurrentCultureIgnoreCase)
            || attr.StartsWith(MSBUILD_PACKAGE_FOLDER2, StringComparison.CurrentCultureIgnoreCase)

    let filterFullBuildTargets (xel : XElement) =
        let attr = !> (xel.Attribute (NsNone + "Project")) : string
        attr.EndsWith(".full-build/full-build.targets", StringComparison.CurrentCultureIgnoreCase)

    let filterNuget (xel : XElement) =
        let attr = !> (xel.Attribute (NsNone + "Project")) : string
        attr.StartsWith("$(SolutionDir)\.nuget\NuGet.targets", StringComparison.CurrentCultureIgnoreCase)

    let filterNugetTarget (xel : XElement) =
        let attr = !> (xel.Attribute (NsNone + "Name")) : string
        String.Equals(attr, "EnsureNuGetPackageBuildImports", StringComparison.CurrentCultureIgnoreCase)

    let filterNugetPackage (xel : XElement) =
        let attr = !> (xel.Attribute (NsNone + "Include")) : string
        String.Equals(attr, "packages.config", StringComparison.CurrentCultureIgnoreCase)

    let filterPaketReference (xel : XElement) =
        let attr = !> (xel.Attribute (NsNone + "Include")) : string
        attr.StartsWith("paket.references", StringComparison.CurrentCultureIgnoreCase)

    let filterPaketTarget (xel : XElement) =
        let attr = !> (xel.Attribute (NsNone + "Project")) : string
        attr.StartsWith("$(SolutionDir)\.paket\paket.targets", StringComparison.CurrentCultureIgnoreCase)

    let filterPaket (xel : XElement) =
        xel.Descendants(NsMsBuild + "Paket").Any ()

    let filterAssemblies (assFiles) (xel : XElement) =
        let inc = !> xel.Attribute(XNamespace.None + "Include") : string
        let assName = inc.Split([| ',' |], StringSplitOptions.RemoveEmptyEntries).[0]
        let assRef = AssemblyId.from (System.Reflection.AssemblyName(assName))
        let res = Set.contains assRef assFiles
        not res

    let hasNoChild (xel : XElement) =
        not <| xel.DescendantNodes().Any()

    let always _ = true

    // cleanup everything that will be modified
    let cproj = XDocument (xproj)

    let seekAndDestroy =
        [ 
            // paket
            "None", filterPaketReference
            "Import", filterPaketTarget
            "Choose", filterPaket

            // project references
            "ProjectReference", always
            
            // unknown assembly references
            "Reference", filterAssemblies project.AssemblyReferences

            // full-build imports
            "Import", filterFullBuildProject
            "Import", filterFullBuildPackage
            "Import", filterFullBuildTargets
            "FBWorkspaceDir", always

            // nuget stuff
            "Import", filterNuget
            "Target", filterNugetTarget
            "None", filterNugetPackage
            "Content", filterNugetPackage

            // cleanup project
            "BaseIntermediateOutputPath", always 
            "SolutionDir", always
            "RestorePackages", always
            "NuGetPackageImportStamp", always
            "ItemGroup", hasNoChild
            "PropertyGroup", hasNoChild
        ]

    seekAndDestroy |> Seq.iter (fun (x, y) -> cproj.Descendants(NsMsBuild + x).Where(y).Remove())
    cproj


let private convertProject (xproj : XDocument) (project : Project) =
    let setOutputPath (xel : XElement) =
        xel.Value <- BIN_FOLDER

    let setDocumentation (xel : XElement) =
        let fileName = sprintf "%s/%s.xml" BIN_FOLDER project.ProjectId.toString
        xel.Value <- fileName

    let filterAssemblyInfo (xel : XElement) =
        let fileName = !> xel.Attribute(XNamespace.None + "Include") : string
        fileName.IndexOf("AssemblyInfo.", StringComparison.CurrentCultureIgnoreCase) <> -1

    let rec patchAssemblyVersion (lines : string list) =
        match lines with
        | line :: tail -> if line.Contains("AssemblyVersion") || line.Contains("AssemblyFileVersion") then 
                              patchAssemblyVersion tail
                          else 
                              line :: patchAssemblyVersion tail
        | [] -> []

    let patchAssemblyInfo (xel : XElement) =
        let fileName = !> xel.Attribute(XNamespace.None + "Include") : string
        let repoDir = Env.GetFolder Folder.Workspace |> GetSubDirectory project.Repository.toString
        let prjFile = repoDir |> GetFile project.RelativeProjectFile.toString 
        let prjDir = Path.GetDirectoryName (prjFile.FullName) |> DirectoryInfo                       
        let infoFile = prjDir |> GetFile fileName
        let content = File.ReadAllLines (infoFile.FullName) |> List.ofSeq
                                                            |> patchAssemblyVersion
        File.WriteAllLines(infoFile.FullName, content)

    let cproj = cleanupProject xproj project

    // set assembly info
    cproj.Descendants(NsMsBuild + "Compile").Where(filterAssemblyInfo)
                                            |> Seq.iter patchAssemblyInfo

    // set OutputPath
    cproj.Descendants(NsMsBuild + "OutputPath") |> Seq.iter setOutputPath
    cproj.Descendants(NsMsBuild + "DocumentationFile") |> Seq.iter setDocumentation
    cproj.Descendants(NsMsBuild + "AssemblyName") |> Seq.iter (fun x -> x.Value <- project.Output.toString)
    cproj.Descendants(NsMsBuild + "AutoGenerateBindingRedirects") |> Seq.iter (fun x -> x.Value <- "false")

    // TODO: both 3 fields are optional - must discard everything and reset everything if not null or empty
    if project.FxVersion.toString <> null then
        cproj.Descendants(NsMsBuild + "TargetFrameworkVersion") |> Seq.iter (fun x -> x.Value <- project.FxVersion.toString)
    if project.FxProfile.toString <> null then
        cproj.Descendants(NsMsBuild + "TargetFrameworkProfile") |> Seq.iter (fun x -> x.Value <- project.FxProfile.toString)
    if project.FxIdentifier.toString <> null then
        cproj.Descendants(NsMsBuild + "TargetFrameworkIdentifier") |> Seq.iter (fun x -> x.Value <- project.FxIdentifier.toString)

    // import fb target
    let wbRelative = ComputeHops (sprintf "%s/%s" project.Repository.toString project.RelativeProjectFile.toString)
    let firstItemGroup = cproj.Descendants(NsMsBuild + "ItemGroup").First()
    let importFB = XElement (NsMsBuild + "Import",
                       XAttribute (NsNone + "Project", 
                                   sprintf "%s.full-build/full-build.targets" wbRelative))
    firstItemGroup.AddBeforeSelf (importFB)

    // add project references
    for projectReference in project.ProjectReferences do
        let prjRef = projectReference.toString
        let importFile = sprintf "%s%s.targets" MSBUILD_PROJECT_FOLDER prjRef
        let import = XElement (NsMsBuild + "Import",
                        XAttribute (NsNone + "Project", importFile))
        cproj.Root.LastNode.AddAfterSelf(import)

    // add nuget references
    for packageReference in project.PackageReferences do
        let pkgId = packageReference.toString
        let importFile = sprintf "%s%s/package.targets" MSBUILD_PACKAGE_FOLDER pkgId
        let import = XElement (NsMsBuild + "Import",
                        XAttribute (NsNone + "Project", importFile))
        cproj.Root.LastNode.AddAfterSelf(import)
    cproj

let private convertProjectContent (xproj : XDocument) (project : Project) =
    let convxproj = convertProject xproj project
    convxproj

let ConvertProjects projects xdocLoader xdocSaver =
    let wsDir = Env.GetFolder Env.Folder.Workspace
    for project in projects do
        let repoDir = wsDir |> GetSubDirectory (project.Repository.toString)
        if repoDir.Exists then
            let projFile = repoDir |> GetFile project.RelativeProjectFile.toString 
            let maybexproj = xdocLoader projFile
            match maybexproj with
            | Some xproj -> let convxproj = convertProjectContent xproj project
                            xdocSaver projFile convxproj
            | _ -> failwithf "Project %A does not exist" projFile

let GenerateProjects (projects : Project seq) (xdocSaver : FileInfo -> XDocument -> Unit) =
    let prjDir = Env.GetFolder Env.Folder.Project
    for project in projects do
        let refProjectContent = generateProjectTarget project
        let projectFile = prjDir |> GetFile (AddExt Targets (project.Output.toString))
        xdocSaver projectFile refProjectContent

        let refProjectCopyContent = generateProjectCopyTarget project
        let projectCopyFile = prjDir |> GetFile (AddExt Targets (project.Output.toString + "-copy"))
        xdocSaver projectCopyFile refProjectCopyContent

let RemoveUselessStuff (projects : Project set) =
    let wsDir = Env.GetFolder Env.Folder.Workspace
    let seekAndDestroyFiles = [
                                "*.sln"
                                "packages.config"
                                "paket.dependencies"
                                "paket.lock"
                                "paket.references"
                              ]
    let seekAndDestroyDirs = [
                                "packages"
                                ".paket"
                                ".nuget"
                             ]

    let repos = projects |> Set.map (fun x -> x.Repository)
    for repo in repos do
        let repoDir = wsDir |> GetSubDirectory (repo.toString)
        if repoDir.Exists then
            seekAndDestroyFiles |> Seq.iter (fun x -> repoDir.EnumerateFiles (x, SearchOption.AllDirectories)
                                                      |> Seq.iter (fun x -> x.Delete()))
            seekAndDestroyDirs |> Seq.iter (fun x -> repoDir.EnumerateDirectories (x, SearchOption.AllDirectories)
                                                     |> Seq.iter (fun x -> x.Delete(true)))

    for project in projects do
        let prjDir = wsDir |> GetSubDirectory (project.relativeProjectFolderFromWorkspace)
        if prjDir.Exists then 
            let binDir = prjDir |> GetSubDirectory BIN_FOLDER
            let objDir = prjDir |> GetSubDirectory OBJ_FOLDER
            binDir |> IoHelpers.ForceDelete
            objDir |> IoHelpers.ForceDelete
