﻿module ProjectParserTests

open System
open System.IO
open System.Linq
open System.Xml.Linq
open ProjectParser
open NUnit.Framework
open FsUnit
open Anthology


let XDocumentLoader (fi : FileInfo) : XDocument =
    let fileName = match fi.Name with
                   | "packages.config" -> "packages.xml"
                   | x -> x
    XDocument.Load (fileName)

[<Test>]
let CheckBasicParsingCSharp () =
    let expectedPackages = [ { Id="FSharp.Data"; Version="2.2.5"; TargetFramework="net45" }
                             { Id="FsUnit"; Version="1.3.0.1"; TargetFramework="net45" }
                             { Id="Mini"; Version="0.4.2.0"; TargetFramework="net45" }
                             { Id="Newtonsoft.Json"; Version="7.0.1"; TargetFramework="net45" }
                             { Id="NLog"; Version="4.0.1"; TargetFramework="net45" }
                             { Id="NUnit"; Version="2.6.3"; TargetFramework="net45" }
                             { Id="xunit"; Version="1.9.1"; TargetFramework="net45" } ]
    
    let file = new FileInfo ("./CSharpProjectSample1.xml")
    let prjDescriptor = ProjectParser.ParseProjectContent XDocumentLoader file.Directory file
    prjDescriptor.Project.ProjectGuid |> should equal (ProjectParser.ParseGuid "3AF55CC8-9998-4039-BC31-54ECBFC91396")
    prjDescriptor.Packages |> should equal expectedPackages

[<Test>]
let CheckBasicParsingFSharp () =
    let file = new FileInfo ("./FSharpProjectSample1.xml")
    let prjDescriptor = ProjectParser.ParseProjectContent XDocumentLoader file.Directory file
    prjDescriptor.Project.ProjectGuid |> should equal (ProjectParser.ParseGuid "5fde3939-c144-4287-bc57-a96ec2d1a9da")

[<Test>]
let CheckParseVirginProject () =
    let file = new FileInfo ("./VirginProject.xml")
    let prjDescriptor = ProjectParser.ParseProjectContent XDocumentLoader file.Directory file
    prjDescriptor.Project.ProjectReferences |> should equal [ProjectParser.ParseGuid "c1d252b7-d766-4c28-9c46-0696f896846d"]

[<Test>]
let CheckParseConvertedProject () =
    let file = new FileInfo ("./ConvertedProject.xml")
    let prjDescriptor = ProjectParser.ParseProjectContent XDocumentLoader file.Directory file
    prjDescriptor.Project.ProjectReferences |> should equal [ProjectParser.ParseGuid "6f6eb447-9569-406a-a23b-c09b6dbdbe10"]
