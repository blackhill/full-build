//   Copyright 2014-2016 Pierre Chalamet
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

module ArtifactFile

open System.IO
open IoHelpers
open Anthology



let publishFile (branch : string option) buildnum hash =
    let antho = Configuration.LoadAnthology ()
    let mainRepo = antho.MasterRepository
    let wsDir = Env.GetFolder Env.Workspace
    let versionDir = DirectoryInfo(antho.Artifacts.Uri) |> GetSubDirectory hash
    let tmpVersionDir = DirectoryInfo(versionDir.FullName + ".tmp")
    if tmpVersionDir.Exists then
        tmpVersionDir.Delete(true)

    try
        let doPublish = not versionDir.Exists
        if doPublish then            
            let sourceBinDir = Env.GetFolder Env.Bin
            let targetBinDir = tmpVersionDir |> GetSubDirectory Env.PUBLISH_BIN_FOLDER
            IoHelpers.CopyFolder sourceBinDir targetBinDir true

            let appTargetDir = tmpVersionDir |> GetSubDirectory Env.PUBLISH_APPS_FOLDER
            let appDir = Env.GetFolder Env.AppOutput
            IoHelpers.CopyFolder appDir appTargetDir true

            // publish
            Try (fun () -> Vcs.VcsPush wsDir antho.Vcs mainRepo)

            tmpVersionDir.MoveTo(versionDir.FullName)
        else
            printfn "[WARNING] Build output already exists - skipping"

        let latestVersionFile = DirectoryInfo(antho.Artifacts.Uri) |> GetFile "versions"
        let version = match branch with
                      | None -> sprintf "%s:%s:default" buildnum hash
                      | Some br -> sprintf "%s:%s:%s" buildnum hash br
        File.AppendAllLines(latestVersionFile.FullName, [version])
        printfn "[version] %s" hash
    with
        _ -> versionDir.Refresh ()
             if versionDir.Exists then versionDir.MoveTo(versionDir.FullName + ".failed")

             tmpVersionDir.Refresh()
             if tmpVersionDir.Exists then tmpVersionDir.Delete(true)

             reraise ()


let publishPullRefBinFile version =
    let antho = Configuration.LoadAnthology ()
    let artifactDir = antho.Artifacts.Uri |> DirectoryInfo

    let versionDir = artifactDir |> GetSubDirectory version
    if versionDir.Exists then
        DisplayHighlight (sprintf "Getting binaries %s" version)
        let sourceBinDir = versionDir |> GetSubDirectory Env.PUBLISH_BIN_FOLDER
        let targetBinDir = Env.GetFolder Env.Bin
        IoHelpers.CopyFolder sourceBinDir targetBinDir false
    else
        DisplayHighlight "[WARNING] No reference binaries found"


let publishPullLatestRefBinFile () =
    let antho = Configuration.LoadAnthology ()
    let versionsFile = DirectoryInfo(antho.Artifacts.Uri) |> GetFile "versions"
    let version = File.ReadAllLines(versionsFile.FullName) |> Seq.last
    let hash = version.Split(':') |> Seq.toArray
    publishPullRefBinFile hash.[1]

