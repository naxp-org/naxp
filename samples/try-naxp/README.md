# try-naxp

A console project that consumes the `naxp` package the way anyone else would, so that the source generator is exercised through what actually ships rather than through the projects next door.

## Running it

Pack the library, which also packs the generator into `analyzers/dotnet/cs`:

```bash
dotnet pack src/cs/Naxp/Naxp.csproj -c Release -o artifacts/packages
```

Then, from this folder:

```bash
dotnet run
```

`nuget.config` here points at `artifacts/packages` and clears every other source, so nothing is fetched from the network. The path is relative, so a fresh clone needs no editing.

## Reading the generated code

`EmitCompilerGeneratedFiles` is on, so the generator's output is written to:

```
samples/try-naxp/obj/Debug/net8.0/generated/Naxp.Generator/LogMu.Generator.NaxpGenerator/
```

`NaxpAttribute.g.cs` is the attribute, injected into this project rather than referenced from the library. The other file holds the recognisers and codecs. In Visual Studio the same files are under Dependencies, Analyzers, Naxp.Generator.

## After changing the generator

NuGet caches a package by its id and version, so a second `dotnet pack` at the same version changes nothing here and you will keep building the old generator. Either delete the extracted copy:

```bash
Remove-Item -Recurse -Force "$env:USERPROFILE\.nuget\packages\naxp\0.5.0-alpha"
```

or pack a new version with `-p:Version=0.5.0-alpha.2` and change the `PackageReference` to match.

Visual Studio holds analyzer assemblies open while a solution is loaded, so a rebuilt generator may need the project unloaded and reloaded, or VS restarted, before its output changes.

## Seeing the diagnostics

Break something and build. A naxp that does not parse, a type without `partial`, a value type too narrow for the naxp: each is refused with an identifier from NAXP0001 upwards, pointing at the character at fault rather than at the attribute.

```csharp
[Naxp(@"\A\9{2-5}", typeof(int))]      // NAXP0101, on the hyphen
[Naxp(@"\A\9", typeof(string))]        // NAXP0007, on typeof(string)
[Naxp(@"\A\9", typeof(byte))]          // NAXP0008, naming short as the narrowest that fits
```

## Not in the solution

This project is deliberately outside `src/cs/Naxp.slnx`. Building the solution would otherwise need a packed `naxp.0.5.0-alpha.nupkg` to exist, which a fresh clone has not got.
