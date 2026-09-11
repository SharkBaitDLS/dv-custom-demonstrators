## Description

This is a mod for Derail Valley to customize Demonstrator and garage spawns with modded rolling stock. For details, see [the mod page on Nexus Mods](https://www.nexusmods.com/derailvalley/mods/1546).

## API for other mods

When the player presses one of the force respawn buttons in the settings, this mod tears down and rebuilds the affected locomotives. Any references another mod holds to those cars are stale afterwards. To be told when that happens subscribe to `CustomDemonstrators.Api.ForceApplyEvents.Applied`:

```csharp
using CustomDemonstrators.Api;

ForceApplyEvents.Applied += kind =>
{
    if (kind == ForceApplyKind.Demonstrators)
        ReattachMyDemonstratorState();
};
```

The event is raised on the main thread once the respawn has finished, so the new cars and the comms radio are already in their final state when the event fires.

### Subscribing by reflection

If you'd rather not take a hard dependency on this mod, the same event can be subscribed to by reflection. Note that `Delegate.CreateDelegate` accepts a handler taking the enum's underlying `int` in place of a `ForceApplyKind`, which saves you from having to construct a delegate over a type you can't name:

```csharp
using System;
using System.Reflection;
using UnityModManagerNet;

private static int _demonstratorsKind = -1;

private static void SubscribeToForceApply()
{
    var mod = UnityModManager.FindMod("CustomDemonstrators");
    if (mod == null || !mod.Active || !mod.HasAssembly) return;

    var events = mod.Assembly.GetType("CustomDemonstrators.Api.ForceApplyEvents");
    var kindType = mod.Assembly.GetType("CustomDemonstrators.Api.ForceApplyKind");
    var applied = events?.GetEvent("Applied");
    if (applied == null || kindType == null) return;

    _demonstratorsKind = (int)Enum.Parse(kindType, "Demonstrators");

    applied.AddEventHandler(null, Delegate.CreateDelegate(applied.EventHandlerType,
        typeof(MyMod).GetMethod(nameof(OnForceApplied), BindingFlags.NonPublic | BindingFlags.Static)));
}

private static void OnForceApplied(int kind)
{
    if (kind == _demonstratorsKind)
        ReattachMyDemonstratorState();
}
```

Call this from somewhere that runs after this mod has loaded — its `Load` if you list `CustomDemonstrators` in your `LoadAfter`, or lazily on first use if you'd rather not care about mod order. To unsubscribe later, hold onto the delegate you passed to `AddEventHandler` and hand it to `RemoveEventHandler`.

### Re-reading this mod's save metadata

Everything this mod remembers about a save is written under keys beginning with `CustomDemonstrators_`,
and it reads them while the save is loading. A mod bringing demonstrators over from another save can have
that record brought with them, and then ask for it to be read again.

Do not copy the record key by key. The demonstrator fingerprint is one string covering every slot at once,
so copying it would have the loaded save claim a configuration for demonstrators whose locomotives never
moved, and the record would be describing a world that isn't there. `MergeDemonstratorsFrom` rebuilds it
entry by entry instead, and splits the slot placements and parts cargo choices the same way. 

`CargoIds` / `CargoValues` are reconciled rather than adopted, since the cargo types registered with the
game this session are already holding the numbers it handed out. Those types are re-pointed to whatever
the table now gives them, so a rewritten table takes effect immediately rather than at the next load.
The one thing that cannot move is a number a car in the world is carrying, because a car records the
number rather than the cargo, so the table is corrected to match.

```csharp
using CustomDemonstrators.Api;

// The demonstrators being restored take their entry from the other save; every other slot keeps what this
// save already said about it. saveIds are LocoRestorationController.SaveIDs as they appear in that save.
SaveRecord.MergeDemonstratorsFrom(earlierSave, saveIds);
SaveRecord.Reload();
```

### Bringing one forward into a new slot

A demonstrator going back into the slot it came from displaces whatever stands in that slot now, which may
be a restoration the player has put work into. `SlotOccupants` allows you to preview what that displacement
would be, if any.

A locomotive can be given a demonstrator slot of its own instead (if the museum has space available),
leaving its old slot and the new loco in it untouched:

```csharp
// Ask before merging rather than after: both answers describe the world as it currently stands.
var displaces = SaveRecord.SlotOccupants(earlierSave, saveIds);
var reasons = SaveRecord.NewSlotEligibility(earlierSave, saveIds);  // null against one that can have a slot

SaveRecord.MergeDemonstratorsFrom(earlierSave, inPlace, asNewSlots);
SaveRecord.Reload();
```

### Doing it by reflection

To avoid a hard dependency on this mod:

```csharp
var mod = UnityModManager.FindMod("CustomDemonstrators");
var api = mod != null && mod.Active && mod.HasAssembly
    ? mod.Assembly.GetType("CustomDemonstrators.Api.SaveRecord")
    : null;
if (api != null)
{
    api.GetMethod("MergeDemonstratorsFrom", BindingFlags.Public | BindingFlags.Static)
        ?.Invoke(null, [earlierSave, saveIds]);
    api.GetMethod("Reload", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
}
```

Bind the three-argument `MergeDemonstratorsFrom` by its signature, since it overloads the two-argument one:

```csharp
api.GetMethod("MergeDemonstratorsFrom", BindingFlags.Public | BindingFlags.Static, null,
    [typeof(JObject), typeof(IEnumerable<string>), typeof(IEnumerable<string>)], null);
```

A build of this mod that predates any of these returns `null` from `GetMethod` rather than failing, so
check for that and fall back to leaving the record for the next load.

## Building

Building the project requires some initial setup, after which running `dotnet build` will do a Debug build or running `dotnet build -c Release` will do a Release build.

### References Setup

After cloning the repository, some setup is required in order to successfully build the mod DLLs. You will need to create a new [Directory.Build.targets][references-url] file to specify your local reference paths. This file will be located in the main directory, next to CustomDemonstrators.sln.

Below is an example of the necessary structure. When creating your targets file, you will need to replace the reference paths with the corresponding folders on your system. Make sure to include semicolons **between** each of the paths and no semicolon after the last path. Also note that any shortcuts you might use in file explorer—such as %ProgramFiles%—won't be expanded in these paths. You have to use full, absolute paths.
```xml
<Project>
	<PropertyGroup>
		<ReferencePath>
			C:\Program Files (x86)\Steam\steamapps\common\Derail Valley\DerailValley_Data\Managed\
		</ReferencePath>
		<AssemblySearchPaths>
			$(AssemblySearchPaths);$(ReferencePath);$(ReferencePath.Trim())..\..\Mods\DVLangHelper\;$(ReferencePath.Trim())..\..\Mods\DVCustomCarLoader\
		</AssemblySearchPaths>
	</PropertyGroup>
</Project>
```

## Packaging

To package a build for distribution, you can run the `package.ps1` PowerShell script in the root of the project. If no parameters are supplied, it will create a .zip file ready for distribution in the dist directory. A post build event is configured to run this automatically after each successful Release build.

Linux: `pwsh ./package.ps1`
Windows: `powershell -executionpolicy bypass .\package.ps1`

### Parameters

Some parameters are available for the packaging script.

#### -NoArchive

Leave the package contents uncompressed in the output directory.

#### -OutputDirectory

Specify a different output directory.
For instance, this can be used in conjunction with `-NoArchive` to copy the mod files into your Derail Valley installation directory.

#### -ArchiveSuffix

Append a suffix to the archive's file name, e.g. `-ArchiveSuffix Debug` writes `dist/CustomDemonstratorsDebug.zip`.

## License

Source code is distributed under the MIT license.
See [LICENSE][license-url] for more information.

[license-url]: https://github.com/SharkBaitDLS/dv-stock-car-remover/blob/main/LICENSE
[references-url]: https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-your-build?view=vs-2022
