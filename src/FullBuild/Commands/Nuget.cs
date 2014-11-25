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
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using FullBuild.Config;
using FullBuild.Helpers;
using FullBuild.Model;
using NLog;

namespace FullBuild.Commands
{
    internal static class NuGet
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public static void InstallPackage(Package pkg)
        {
            var config = ConfigManager.LoadConfig(WellKnownFolders.GetWorkspaceDirectory());

            var cacheDir = WellKnownFolders.GetCacheDirectory();
            if (! string.IsNullOrEmpty(config.PackageGlobalCache))
            {
                cacheDir = new DirectoryInfo(config.PackageGlobalCache);
                cacheDir.Create();
            }

            // avoid downloading again the package (they are immutable)
            var pkgFile = new FileInfo(Path.Combine(cacheDir.FullName, string.Format("{0}.{1}.zip", pkg.Name, pkg.Version)));
            if (!pkgFile.Exists)
            {
                DownloadNugetPackage(pkg, pkgFile, config.Nugets);
            }

            var pkgsDir = WellKnownFolders.GetPackageDirectory();
            var pkgDir = pkgsDir.GetDirectory(pkg.Name);
            if (pkgDir.Exists)
            {
                pkgDir.Delete(true);
            }

            pkgDir.Create();

            try
            {
                _logger.Debug("Unzipping package {0}:{1}", pkg.Name, pkg.Version);
                ZipFile.ExtractToDirectory(pkgFile.FullName, pkgDir.FullName);
            }
            catch(Exception)
            {
                // file is probably corrupt...
                pkgFile.Delete();
                throw;
            }
        }

        private static void DownloadNugetPackage(Package pkg, FileInfo pkgFile, string[] nugets)
        {
            foreach(var nuget in nugets)
            {
                try
                {
                    DownloadNugetPackage(pkg, pkgFile, nuget);
                    return;
                }
                catch(Exception ex)
                {
                    _logger.Debug("Download error", ex);
                    _logger.Debug("Failed to download package {0} {1} from {2}", pkg.Name, pkg.Version, nuget);
                }
            }

            var msg = string.Format("Failed to download package {0} {1} from provided locations", pkg.Name, pkg.Version);
            throw new ArgumentException(msg);
        }

        private static void DownloadNugetPackage(Package pkg, FileInfo pkgFile, string nuget)
        {
            var packageUrl = null != pkg.Version
                ? string.Format("{0}package/{1}/{2}", nuget, pkg.Name, pkg.Version)
                : string.Format("{0}package/{1}", nuget, pkg.Name);

            _logger.Debug("Downloading package {0}:{1} from {2}", pkg.Name, pkg.Version, packageUrl);
            var webClient = new WebClient();
            webClient.DownloadFile(packageUrl, pkgFile.FullName);
        }

        public static string GetLatestPackageVersion(string name, string[] nugets)
        {
            foreach(var nuget in nugets)
            {
                var ctx = new NuGetService.V2FeedContext(new Uri(nuget));
                var nugetPkg = (from p in ctx.Packages
                                where p.Id == name && p.IsLatestVersion && ! p.IsPrerelease
                                select p).SingleOrDefault();
                if (null != nugetPkg)
                {
                    return nugetPkg.Version;
                }
            }

            return null;
        }
    }
}