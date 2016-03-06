module Forge.SolutionSystem

open System
open System.IO
open FParsec



type UserState = unit
type Parser<'t> = Parser<'t, UserState>

/// Sets the platform for a Build Configuration
///     x86,  x64, or AnyCPU.
/// The default is AnyCPU.
type PlatformType =
    | X86 |  X64 | AnyCPU

    override self.ToString () = self |> function
        | X86     -> Constants.X86
        | X64     -> Constants.X64
        | AnyCPU  -> Constants.AnyCPU

    static member Parse text = text |> function
        | InvariantEqual Constants.X86     -> X86
        | InvariantEqual Constants.X64     -> X64
        | InvariantEqual Constants.AnyCPU  -> AnyCPU
        | _ ->
            failwithf "Could not parse '%s' into a `PlatformType`" text

    static member TryParse text = text |> function
        | InvariantEqual Constants.X86     -> Some X86
        | InvariantEqual Constants.X64     -> Some X64
        | InvariantEqual Constants.AnyCPU  -> Some AnyCPU
        | _                 -> None


type SolutionItem = 
    {   Name: string
        Path: string 
    }


type PreOrPostProject =
    | PreProject | PostProject

type SolutionItemsProjectSection = 
    {   Items: SolutionItem list
        PreOrPost: PreOrPostProject 
    }


[<RequireQualifiedAccess>]
type SolutionProjectSection = SolutionItemsProjectSection


type PreOrPostSolution =
    | PreSolution | PostSolution


type BuildConfiguration = 
    | Debug | Release


type SolutionConfigurationPlatform =
    {   Configuration: BuildConfiguration;
        Platform: PlatformType 
    }

type SolutionConfigurationPlatformsGlobalSection = 
    {   PreOrPost: PreOrPostSolution 
        ConfigurationPlatforms: SolutionConfigurationPlatform list 
    }
    

type ProjectConfigurationPlatform =
    {   ProjectGuid: Guid
        BuildConfiguration: BuildConfiguration
        ProjectConfiguration: string
        Platform: PlatformType 
    }


type ProjectConfigurationPlatformsGlobalSection =
    {   PreOrPost: PreOrPostSolution 
        ConfigurationPlatforms: ProjectConfigurationPlatform list 
    }
    

type SolutionProperty = 
    {   Name: string
        Value: string 
    }


type SolutionPropertiesGlobalSection = 
    {   PreOrPost: PreOrPostSolution 
        Properties: SolutionProperty list 
    }

type NestedProject =
    {   ProjectGuid: Guid
        ProjectTypeGuid: Guid 
    }

type NestedProjectsGlobalSection = 
    {   PreOrPost: PreOrPostSolution 
        Projects: NestedProject list 
    }


type GlobalSection = 
    | SolutionConfigurationPlatformsGlobalSection of SolutionConfigurationPlatformsGlobalSection
    | ProjectConfigurationPlatformsGlobalSection of ProjectConfigurationPlatformsGlobalSection
    | SolutionPropertiesGlobalSection of SolutionPropertiesGlobalSection
    | NestedProjectsGlobalSection of NestedProjectsGlobalSection


[<RequireQualifiedAccess>]
type SolutionGlobal = 
    { GlobalSections: GlobalSection list }


[<RequireQualifiedAccess>]
type SolutionProject =
    {   ProjectTypeGuid: Guid 
        Path: string
        RelativePath: string
        GUID : Guid
        ProjectSections: SolutionProjectSection list 
    }

[<AutoOpen>]
module internal Parsers =
    let ``,`` : Parser<_> = pchar ','
    let pEq    : Parser<_> = pchar '='
    let ``-`` : Parser<_> = pchar '-'
    let ``{`` : Parser<_> = pchar '{'
    let ``}`` : Parser<_> = pchar '}'
    let ``"`` : Parser<_> = pchar '"'
    let ``|`` : Parser<_> = pchar '|'
    let ``.`` : Parser<_> = pchar '.'
    let ``(`` : Parser<_> = pchar '('
    let ``)`` : Parser<_> = pchar ')'

type SolutionFile = 
    {   Projects: SolutionProject list
        Global: SolutionGlobal 
    }



    static member parseFile (filename: string) = File.ReadAllText filename |> SolutionFile.parseString

    static member parseString (input: string) =         
        let pGuid: Parser<Guid> = ``{`` >>. manyChars (``-`` <|> hex) .>> ``}`` |>> Guid.Parse
        
        let pQuoted (p: Parser<'a>) : Parser<'a> = pchar '"' >>. p .>> ``"``
        
        let pQuotedString: Parser<string> = pQuoted (manySatisfy ((<>) '"'))
        
        let pEndGlobalSection: Parser<string> = spaces >>. pstring "EndGlobalSection" .>> spaces


        let pSolutionItem: Parser<SolutionItem> = 
            let pName: Parser<string>   = spaces >>. manyCharsTill anyChar (spaces >>. pEq)
            let pPath: Parser<string>   = spaces >>. manyCharsTill anyChar newline
            pName .>>. pPath |>> fun (name, path) -> 
                {   Name = name 
                    Path = path 
                }


        let pProjectSection: Parser<SolutionItemsProjectSection> =
            let pPreOrPostProject: Parser<PreOrPostProject> = 
                choice [
                    pstring "preProject" >>% PreProject
                    pstring "postProject" >>% PostProject
                ]

            let pProjectSectionHeader: Parser<PreOrPostProject> = 
                spaces >>. skipString "ProjectSection(SolutionItems)" >>. spaces >>. pEq >>. spaces >>. pPreOrPostProject .>> spaces

            let pProjectSectionFooter: Parser<string> =
                (spaces >>. pstring "EndProjectSection" .>> spaces)
            let pSolutionItems: Parser<SolutionItem list> =
                many (notFollowedBy pProjectSectionFooter >>. (pSolutionItem .>> spaces))

            pProjectSectionHeader .>>.  (pSolutionItems .>> pProjectSectionFooter) 
            |>> fun (preOrPost, items) -> 
                {Items = items; PreOrPost = preOrPost}

        let pSolutionProject: Parser<SolutionProject> =
            pipe5 (spaces >>. pstring "Project(" >>. pQuoted pGuid .>> ``)`` .>> spaces .>> pEq)
                  (spaces >>. pQuotedString .>> ``,``) 
                  (spaces >>. pQuotedString .>> ``,``) 
                  (spaces >>. pQuoted pGuid .>> spaces) 
                  (manyTill pProjectSection (pstring "EndProject"))
                  (fun projectGuid path relPath projectTypeGuid sections -> 
                    {   ProjectTypeGuid = projectTypeGuid
                        Path = path
                        RelativePath = relPath
                        GUID = projectGuid 
                        ProjectSections = sections
                    })

        let pPreOrPostSolution: Parser<PreOrPostSolution> = 
            choice [
                pstring "preSolution" >>% PreSolution
                pstring "postSolution" >>% PostSolution
            ]


        let pSolutionPropertiesGlobalSection: Parser<SolutionPropertiesGlobalSection> =
            
            let pHeader: Parser<PreOrPostSolution> = 
                pstring "GlobalSection(SolutionProperties)" >>. spaces >>. pEq >>. spaces >>. pPreOrPostSolution .>> spaces

            let pSolutionProperty: Parser<SolutionProperty> = 
                let pName : Parser<String> = spaces >>. manyCharsTill anyChar (spaces >>. pEq)
                let pValue: Parser<String> = spaces >>. manyCharsTill anyChar newline
                pName .>>. pValue 
                |>> fun (name, value) -> 
                    {   Name = name 
                        Value = value
                    }

            pHeader .>>. many (notFollowedBy pEndGlobalSection >>. pSolutionProperty) .>> pEndGlobalSection 
            |>> fun (preOrPost, properties) -> 
                {   PreOrPost = preOrPost 
                    Properties = properties
                }


        let pNestedProjectsGlobalSection: Parser<NestedProjectsGlobalSection> =
            
            let pHeader: Parser<PreOrPostSolution> = 
                pstring "GlobalSection(NestedProjects)" >>. spaces >>. pEq 
                >>. spaces >>. pPreOrPostSolution .>> spaces
            
            let pNestedProject: Parser<NestedProject> = 
                (spaces >>. pGuid) .>>. (spaces >>. pEq >>. spaces >>. pGuid)
                |>> fun (projectGuid, projectTypeGuid) -> 
                    {   ProjectGuid = projectGuid
                        ProjectTypeGuid = projectTypeGuid
                    }

            pHeader .>>. many (notFollowedBy pEndGlobalSection >>. pNestedProject) .>> pEndGlobalSection 
            |>> fun (preOrPost, nestedProjects) -> 
                {   PreOrPost = preOrPost
                    Projects = nestedProjects
                }


        let pBuildConfiguration: Parser<BuildConfiguration> = // TODO this does not cover all the possibilities
            (pstring "Debug" >>% Debug) <|> (pstring "Release" >>% Release)


        let pSolutionConfigurationPlatformsGlobalSection: Parser<SolutionConfigurationPlatformsGlobalSection> =
            
            let pHeader: Parser<PreOrPostSolution> = 
                spaces >>. pstring "GlobalSection(SolutionConfigurationPlatforms)" 
                >>. spaces >>. pEq >>. spaces >>. pPreOrPostSolution .>> spaces
           
            let pSolutionConfigurationPlatform: Parser<SolutionConfigurationPlatform> = 
                let pJunkInTheMiddle: Parser<string> = ``|`` >>. manyCharsTill anyChar ``|``
                spaces >>. pBuildConfiguration .>>. (pJunkInTheMiddle >>. restOfLine true)
                |>> fun (config, platform) -> 
                    {   Configuration = config
                        Platform = PlatformType.Parse platform
                    }

            pHeader .>>. many (notFollowedBy pEndGlobalSection >>. pSolutionConfigurationPlatform) .>> pEndGlobalSection 
            |>> fun (preOrPost, configPlatforms) -> 
                {   PreOrPost = preOrPost 
                    ConfigurationPlatforms = configPlatforms
                }


        let pProjectConfigurationPlatformsGlobalSection: Parser<ProjectConfigurationPlatformsGlobalSection> =
            let pHeader: Parser<PreOrPostSolution> = 
                pstring "GlobalSection(ProjectConfigurationPlatforms)" 
                >>. spaces >>. pEq >>. spaces >>. pPreOrPostSolution .>> spaces

            let pProjectConfigurationPlatform: Parser<ProjectConfigurationPlatform> = 
                pipe4 (spaces >>. pGuid .>> ``.``) 
                    pBuildConfiguration
                    (``|`` >>. manyCharsTill anyChar ``|``) 
                    (manyCharsTill anyChar newline)
                    (fun projectGuid buildConfig projectConfig platform -> 
                        {   ProjectGuid = projectGuid
                            BuildConfiguration = buildConfig
                            ProjectConfiguration = projectConfig
                            Platform = PlatformType.Parse platform
                        })

            pHeader .>>. many (notFollowedBy pEndGlobalSection >>. pProjectConfigurationPlatform) .>> pEndGlobalSection 
            |>> fun (preOrPost, configPlatforms) -> 
                {   PreOrPost = preOrPost 
                    ConfigurationPlatforms = configPlatforms
                }
            

        let pSolutionGlobalSection: Parser<GlobalSection> = 
            choice [
                pSolutionConfigurationPlatformsGlobalSection |>> SolutionConfigurationPlatformsGlobalSection
                pProjectConfigurationPlatformsGlobalSection  |>> ProjectConfigurationPlatformsGlobalSection
                pSolutionPropertiesGlobalSection             |>> SolutionPropertiesGlobalSection
                pNestedProjectsGlobalSection                 |>> NestedProjectsGlobalSection
            ]


        let pSolutionGlobal: Parser<SolutionGlobal> = 
            let pHeader: Parser<string> = spaces >>. pstring "Global" .>> spaces 
            let pFooter: Parser<string> = spaces >>. pstring "EndGlobal" .>> spaces

            pHeader >>. (manyTill pSolutionGlobalSection pFooter) 
            |>> fun sections -> { GlobalSections = sections }


        let pSolutionFile: Parser<SolutionFile> =
            many (notFollowedBy pSolutionProject >>. anyChar) 
            >>. many (notFollowedBy (spaces >>. pstring "Global" .>> spaces)  
            >>. pSolutionProject) .>>. pSolutionGlobal
            |>> fun (projects, solutionGlobal) -> 
                {   Projects = projects
                    Global = solutionGlobal 
                }
            

        match run pSolutionFile input with
        | Success (result, _, _) -> result
        | Failure (error, _, _)  -> failwith error