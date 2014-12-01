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

using System.IO;
using FullBuild.Model;

namespace FullBuild.Helpers
{
    internal static class ProjectExtensions
    {
        public static string GetName(this Project @this)
        {
            return new FileInfo(@this.ProjectFile).Name;
        }

        public static string GetProjectName(this Project @this)
        {
            return Path.GetFileName(@this.ProjectFile);

            //var projectFile = @this.ProjectFile.Split('/')[0];
            //var propName = string.Format("{0} ({1})", @this.AssemblyName, @this.FxTarget);

            //var projectName = projectFile + propName;
            //return projectName;
        }

        public static string GetProjectPropertyGroupName(this Project @this)
        {
            var propName = string.Format("FullBuild_{0}_{1}_Prj", @this.AssemblyName, @this.FxTarget).ToMsBuild();
            return propName;
        }
    }
}
