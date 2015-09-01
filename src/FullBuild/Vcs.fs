﻿// Copyright (c) 2014-2015, Pierre Chalamet
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
module Vcs

open System
open Exec
open IoHelpers
open Anthology
open System.IO

let private GitTip (repoDir : DirectoryInfo) =
    let args = @"log -1 --format=""%H"""
    Exec "git" args repoDir

let private HgTip (repoDir : DirectoryInfo) =
    let args = @"id -i"
    Exec "hg" args repoDir

let private GitCloneRepo (url : string) (target : DirectoryInfo) = 
    let args = sprintf "clone %A %A" url target.FullName
    let currDir = DirectoryInfo(Environment.CurrentDirectory)
    Exec "git" args currDir

let private HgCloneRepo (url : string) (target : DirectoryInfo) = 
    let args = sprintf "clone %A %A" url target.FullName
    let currDir = DirectoryInfo(Environment.CurrentDirectory)
    Exec "hg" args currDir

let VcsCloneRepo (wsDir : DirectoryInfo) (repo : Repository) = 
    let checkoutDir = wsDir |> GetSubDirectory repo.Name.Value
    
    let cloneRepo = 
        match repo.Vcs with
        | VcsType.Git -> GitCloneRepo
        | VcsType.Hg -> HgCloneRepo
    cloneRepo repo.Url.Value checkoutDir

let VcsTip (wsDir : DirectoryInfo) (repo : Repository) = 
    let checkoutDir = wsDir |> GetSubDirectory repo.Name.Value
    
    let tipRepo = 
        match repo.Vcs with
        | VcsType.Git -> GitTip
        | VcsType.Hg -> HgTip
    tipRepo checkoutDir
