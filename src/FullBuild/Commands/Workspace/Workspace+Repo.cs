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
using System.Linq;
using System.Text.RegularExpressions;
using FullBuild.Config;
using FullBuild.Helpers;
using FullBuild.SourceControl;

namespace FullBuild.Commands.Workspace
{
    public partial class Workspace
    {
        public static void ListRepos()
        {
            var config = ConfigManager.LoadConfig();
            var wsDir = WellKnownFolders.GetWorkspaceDirectory();

            // validate first that repos are valid and clone them
            foreach (var repo in config.SourceRepos)
            {
                var repoDir = wsDir.GetDirectory(repo.Name);
                var eol = repoDir.Exists
                    ? "@"
                    : "";
                Console.WriteLine("{0}{1}", repo.Name, eol);
            }
        }

        public static void AddRepo(string name, VersionControlType type, string url)
        {
            var newRepo = new RepoConfig
                          {
                              Name = name,
                              Vcs = type,
                              Url = url
                          };

            var config = ConfigManager.LoadConfig();
            if (config.SourceRepos.Any(x => x.Name.InvariantEquals(name)))
            {
                throw new ArgumentException("Repository already exists");
            }

            config.SourceRepos = config.SourceRepos.Concat(new[] {newRepo}).ToArray();
            ConfigManager.SaveConfig(config);
        }

        public static void CloneRepo(string[] repos)
        {
            var wsDir = WellKnownFolders.GetWorkspaceDirectory();
            var config = ConfigManager.LoadConfig();

            // validate first that repos are valid and clone them
            foreach (var repo in repos)
            {
                var match = "^" + repo + "$";
                var regex = new Regex(match, RegexOptions.IgnoreCase);
                var repoConfigs = config.SourceRepos.Where(x => regex.IsMatch(x.Name));
                if (!repoConfigs.Any())
                {
                    Console.WriteLine("WARNING | No repository found");
                    return;
                }

                foreach (var repoConfig in repoConfigs)
                {
                    var repoDir = wsDir.GetDirectory(repoConfig.Name);
                    if (!repoDir.Exists)
                    {
                        var sourceControl = ServiceActivator<Factory>.Create<ISourceControl>(repoConfig.Vcs.ToString());
                        sourceControl.Clone(repoDir, repoConfig.Name, repoConfig.Url);
                    }
                }
            }
        }
    }
}