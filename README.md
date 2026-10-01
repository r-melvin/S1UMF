# Schedule I Unofficial Mod Fixes (S1UMF)

A small MelonLoader mod with fixes for bugs in other Schedule I mods on the **0.4.7 beta** (0.4.7f6 and f7, IL2CPP).
Each fix targets one exact version of one mod and **switches itself off** if that mod is missing or has a
different version - so when an author ships their own fix, this one steps aside without anything to undo.

These are not missing-member gaps (a mod calling something the 0.4.7 update renamed or removed). Those
belong in [Polyfill](https://github.com/DooDesch-Mods/ScheduleOne-Polyfill), and so do three problems S1UMF used
to fix itself and Polyfill now fixes for everyone ([below](#fixed-by-polyfill)).
What is here are bugs in the mods' own logic that 0.4.7 exposes.

> **S1UMF needs [Polyfill](https://www.nexusmods.com/schedule1/mods/2452) 0.13.0 or newer**, which has the
> fixes listed [below](#fixed-by-polyfill). With an older
> Polyfill, or none, those problems come back and some mods still throw. S1UMF says in its log which Polyfill it
> found and whether it is recent enough.

## What it does for you

With about 60 mods on the 0.4.7 beta, before S1UMF:
- **Loading a save could hang or crash.** Custom NPCs broke loading; EmployeeTweaks overflowed the stack.
- **Start-up and quitting sometimes crashed.** Mods' scans of every loaded assembly could kill the game outright.
- **Mods misbehaved:** K9 dogs stood still, automatic guns fired on their own, dragging guns broke storage
  slots, Big Pimpin escort contracts opened the product screen, the dealer customer list threw, Better
  Products Page threw on new products, and the minimap spammed the log.

Each of these is a fix below. All of them are small, logged, and switch off when the mod they fix changes
version. A few more crashes (Production Expansion Reborn's cleaner, Expanded Storage's start-up, patches running
on the wrong objects) are fixed by the latest Polyfill instead.

## Install

1. **MelonLoader 0.7.3** on Schedule I, IL2CPP (Steam `beta` branch for 0.4.7). On Linux/Proton, add
   `WINEDLLOVERRIDES="version=n,b" %command%` to the game's launch options.
2. **The latest [Polyfill](https://www.nexusmods.com/schedule1/mods/2452)** (`Polyfill.dll` in `Mods/`,
   `Polyfill.Boot.dll` in `Plugins/`). Version 0.13.0 or newer.
3. **S1UMF**: `S1UMF.dll` from [Releases](../../releases) into `Mods/`.
4. Launch. The log shows one line per fix, e.g. `[S1UMF] [K9 Patrol 1.1.0] fixed: ...`, and which
   Polyfill it found: `[Polyfill <version>] has the fixes S1UMF relies on`. `Polyfill ... is older than S1UMF needs`
   means update Polyfill.

Nothing to configure. To remove it, delete `S1UMF.dll`; it changes no files.

## Fixes

| Mod (author) | Version | Symptom | Cause | Fix |
|---|---|---|---|---|
| [K9 Patrol](https://www.nexusmods.com/schedule1/mods/2496) (DropDaDeuce) | 1.1.0 | Dogs never move; log shows `K9 dog is STUCK ... hasPath=False pathPending=True` | `K9NPC.RequestPath` only throttles once the agent *has* a path, so while one is still being computed every call re-issues `SetDestination`, restarting it. When a path takes more than a frame it never finishes. | Skip the request while a path is pending for the same target; throttle it when the target moved, as the mod already does for a live path. |
| [More Guns Forked](https://www.nexusmods.com/schedule1/mods/2528) (SirUncleTyrone, fork of MoreGuns by Voidane) | 1.6.6 | An automatic gun fires on its own as soon as it is selected from the hotbar, until put away | The automatic-fire loop fires every frame `GameInput.GetButton(PrimaryClick)` is true. On 0.4.7 that is a set of held buttons updated by press/release events, and a hotbar click can leave left-click "held". | Only allow automatic fire once left-click has been seen released, or freshly pressed, since the weapon came out. |
| [PhoneScroll](https://www.nexusmods.com/schedule1/mods/1638), [NetEye](https://www.nexusmods.com/schedule1/mods/2150), [ProductManager](https://www.nexusmods.com/schedule1/mods/1626) (V4LEXL) | 1.4 / 1.1.0 / 2.4 | The game can die with a fatal CLR error (`0x80131506`) with nothing in the log, e.g. just after a save loads | They find game types by calling `GetTypes()` on every loaded assembly, interop ones included; that can kill the process outright, past any `try`/`catch`. | Answer each walk up front with its one known 0.4.7 type (`HomeScreen`, `Phone`, `IconGenerator` / `ProductIconManager` / `Registry`), and look appearance types up by name in the game assembly's metadata, so no walk runs. |
| [EmployeeTweaks](https://www.nexusmods.com/schedule1/mods/2002) (k073l) | 1.0.10 | The save never finishes loading (stack overflow on the main thread) | It puts a postfix on the `Awake` of `Property` and four subclasses. On 0.4.7f6 `Bungalow.Awake` and `SewerOffice.Awake` have identical bodies and IL2CPP folded them into one native function, so Harmony detours the same address twice and each detour's original is the other. | Run the same patching, once per native function (read from each method's `Il2CppMethodInfo`). S1UMF initialises early for this. |
| [The Big Pimpin](https://www.nexusmods.com/schedule1/mods/1916) (Virtunerd & Fadestyle) | 1.0.11 | Escort ("date") contracts open the product handover screen; the pimp's icon is missing in Messages | Its patches target 0.4.6's `HandoverScreen.Open(contract, customer, mode, callback)` and name `CreateConversationUI`'s conversation `c`. | Run its escort handover from `Open_Contract` (empty submit, as it did through the old callback); call its icon postfix with 0.4.7's parameters. |
| [Better Products Page](https://www.nexusmods.com/schedule1/mods/2458) (manjaroman2) | 1.1 | Selecting a product made after the save loaded throws `KeyNotFoundException` | Its recipe-path map is filled once, on load. | Build the missing entry with its own builder first. |
| [Police Response Overhaul](https://www.nexusmods.com/schedule1/mods/1202) | 1.1.6 | Officers skip weapon changes | Its guard asks for `PoliceOfficer.belt`, gone on 0.4.7 (the officer keeps a `Law.PoliceBelt` in `PoliceBelt`). | Ask 0.4.7's belt. |
| [HererMiniMap](https://www.nexusmods.com/schedule1/mods/899) | 2.0.1 | "AppIcons container not found" every 2 seconds at the main menu | Its watchdog recreates the phone app when there is no phone. | Stay quiet until a phone exists. |
| game | 0.4.7f6-f7 | The dealer's assign-customer list throws when every customer has a dealer | `CustomerSelector.Open` selects the first visible entry without checking there is one. | Contain that one exception; the list is already open. |
| game | 0.4.7f6-f7 | `NPC.get_ID` throws at load (and in mods walking the NPCs when the loading screen closes) | The game's pooled special customers - and custom NPC templates - have no NPC data until they are used, and the play-mode getter has no null check. | Answer `""` for an NPC with no data, as the game's getter does outside play mode. |
| game, with More Guns | 0.4.7f6-f7 | Dragging or storing some guns and magazines breaks the storage slots | `IntegerItemUI.UpdateUI` on an item that is not an `IntegerItemInstance`. | Draw icon and quantity without the value, and name the item once in the log. |

The log says what happened, one line per fix:

```
[S1UMF] [K9 Patrol 1.1.0] fixed: dogs stuck with a path that never finishes computing
[S1UMF] [MoreGuns] is 1.6.7, fix was written for 1.6.6 - standing down
```

## Fixed by Polyfill

S1UMF used to carry these itself. They are Polyfill's now, because they are not about one mod and every
Polyfill user gets them. They need the latest Polyfill (see [Install](#install)); S1UMF no longer repeats them.

| Mod (author) | Version | Symptom | Polyfill |
|---|---|---|---|
| S1API, Lithium (fork), eMployee, Inventory Expanded, Drug Expansion, Production Expansion Reborn, more | various | Native crashes in unrelated places; a mod's patch running for objects of other classes (`LoadingDock.SetStaticOccupant` is the body of 142 setters; eMployee's `RouteEntryUI.ClearRoute` ran for every UI `GraphicRaycaster`) | The folded-code guard: IL2CPP folds identical functions, so a patch on one runs for all. Polyfill reads which patched methods are shared from the running game and lets each mod patch run only for its own class ([#102](https://github.com/DooDesch-Mods/ScheduleOne-Polyfill/pull/102)). |
| [Production Expansion Reborn](https://www.nexusmods.com/schedule1/mods/2477) (Alduin) | 1.0.2B | The cleaner station's trash bags are not thrown | `TrashManager.CreateTrashBag` lost `startKinematic` on 0.4.7. Polyfill puts the old form back and relays the mod's own patch, including its velocity ([#103](https://github.com/DooDesch-Mods/ScheduleOne-Polyfill/pull/103)). |
| [Expanded Storage Reborn](https://www.nexusmods.com/schedule1/mods/2400) (AlduinFeynDoJun) | 1.0.4 | Some launches the game dies during start-up (`0x80131506`) right after its `CustomerHandoverOpenPatch` log line | Its bulk patch also picked up Polyfill's `Open(StorageEntity)` bridge, which has no native method. Polyfill keeps bridges out of bulk patch targets ([#107](https://github.com/DooDesch-Mods/ScheduleOne-Polyfill/pull/107)). |
| [Expanded Storage Reborn](https://www.nexusmods.com/schedule1/mods/2400) (AlduinFeynDoJun) | 1.0.4 | Extra handover slots never page | `HandoverScreen.Open` was split into `Open_*`, each ending in `OnOpen`. Polyfill runs a patch on `Open` on `OnOpen` (since 0.12.9). |

## Fixed by S1API

S1UMF carried this one too until S1API fixed it in its own code.

| Mod (author) | Version | Symptom | S1API |
|---|---|---|---|
| Any mod adding NPCs through S1API (seen with [The Big Pimpin](https://www.nexusmods.com/schedule1/mods/1916)) | game 0.4.7f6 | The save never finishes loading | A custom NPC's `ConsumeProductBehaviour.OnStartServer` throws when its NPC references were never set; S1API repairs them, or contains that one exception ([ifBars/S1API#335](https://github.com/ifBars/S1API/pull/335)). Needs an S1API with that change; until it is released, use an S1API built from it. |

## Quality-of-life tweaks

Not bugs, but small improvements to how another mod behaves. Each one stands down if that mod's version changes.

| Mod (author) | Version | What changes | Why |
|---|---|---|---|
| [HererMiniMap](https://www.nexusmods.com/schedule1/mods/899) (Jack Herer) | 2.0.1 | The minimap only shows while the game world is visible, and sits behind menus, the phone and the loading screen. | Its canvas is a screen overlay at sorting order 1000, so it appears over the loading screen and stays on top of the pause menu and every other screen. S1UMF moves it below the game's UI and shows it only once a save has loaded and the loading screen has closed. |

## Troubleshooting

### The game dies at random during start-up or when you quit

If the Proton or Windows event log says `The process was terminated due to an internal error in the .NET
Runtime ... 80131506`, at a different point each launch, the .NET runtime is crashing inside its JIT while
mods patch the game. With ~60 mods it hit about half of the launches here. Switching the runtime's tiered
compilation off stopped it:

1. Close the game. Open `Schedule I/MelonLoader/net6/MelonLoader.runtimeconfig.json` in a text editor and
   keep a copy of it.
2. Replace its `"configProperties"` block with exactly this (the last line is the new one):
   ```json
   "configProperties": {
     "System.Globalization.Invariant": true,
     "System.Globalization.PredefinedCulturesOnly": true,
     "System.Reflection.Metadata.MetadataUpdater.IsSupported": false,
     "System.Runtime.TieredCompilation": false
   }
   ```
3. Save and launch. A MelonLoader reinstall replaces the file, so do it again after one.

### A mod still misbehaves

Check the log for the S1UMF line naming that mod. `standing down` means your version of the mod is
not the one the fix was written for - the mod may have fixed it itself. The `[Polyfill ...]` line should say
`has the fixes S1UMF relies on`; if it says `is older than S1UMF needs`, update Polyfill. Otherwise, open an
[issue](https://github.com/r-melvin/S1UMF/issues) with `MelonLoader/Latest.log`.

## Mods it fixes

| Mod | Version | What S1UMF fixes |
|---|---|---|
| K9 Patrol | 1.1.0 | dogs stuck with a path that never finishes |
| More Guns Forked | 1.6.6 | automatic weapons firing on their own; storage breaking when dragging guns (game guard) |
| EmployeeTweaks | 1.0.10 | stack overflow loading a save |
| The Big Pimpin | 1.0.11 | escort contracts; Messages icon; save never finishing loading (its custom NPCs) |
| Police Response Overhaul | 1.1.6 | officers skipping weapon changes |
| Better Products Page | 1.1 | errors selecting a product made after loading |
| HererMiniMap | 2.0.1 | warning every 2 s at the main menu (fix); minimap layering over loading screen and menus (tweak) |
| PhoneScroll / NetEye / ProductManager | 1.4 / 1.1.0 / 2.4 | start-up scan that can kill the game |
| Better Customer List, dealer mods | - | the dealer's customer list erroring with every customer assigned (game guard) |
| Over The Counter, S1API, SmartRestock | - | asking NPCs without data (the game's pooled special customers) for their id at load (game guard) |

## Tested with

An **earlier build of S1UMF** (before the fixes below moved to Polyfill), Schedule I **0.4.7f7** (0.4.7f6 for earlier versions), Steam `beta`, IL2CPP,
MelonLoader 0.7.3, Linux / Proton-CachyOS 11.0, with a **pre-release build of Polyfill** that had the same fixes
the release has. Save loads,
start-ups and play sessions, all of these installed together:

Absorbent Soil 1.3.0, All Your Clients Will Order 1.80.0, Auto Clear Completed Deals 1.4.3, AutoReorder
1.0.2, Bargaining Assistant 1.6, Bars Graphics 1.0.2, Better Counter Offer UI 3.4.1, Better Products Page
1.1, BetterCustomerList 1.0.6, BFG Better Supplier 1.6.5, BFG Smart Deal Location 2.4.5, The Big Pimpin
1.0.11, BusinessEmployment 1.1.2, BusinessTracker 1.0.1, Cartel Influence Enhancements 0.3.0, CityCats
1.0.4, ColoredDeals 1.0.6, Dealer Routes 3.2.0, Delivery Control 0.8.0, Drones 1.2.0, Drug Expansion 1.0.3,
eMployee 2.6.0, EmployeeTweaks 1.0.10, Expanded Storage Reborn 1.0.4, ExpandRecipe-Improved 1.2.1,
ExperienceBar 1.0.4, EZInventory 1.3.5, Forklift & Pallets 1.2.0, FurnitureDelivery 2.0.5, HererMiniMap
2.0.1, Inventory Expanded - Backpack & Inventory QoL 1.2.6, K9 Patrol 1.1.0, Laundry App 1.1.9, Lithium
(fork) 1.0.9.6, MapRegionNames 1.0.1, MeshVault 1.0.8, MLVScan 2.1.6, Mod Manager & Phone App 2.2.4, More
Guns Forked 1.6.6, More Save Backups 1.0.3, NetEye 1.1.0, OTC Loader 1.0.5, OverTheCounter 2.0.10 (0.4.7
port), Personnel 2.2.0, Pocket Shop 1.2.0, POI Compass Markers 1.0.7, Police Response Overhaul 1.1.6,
Production Expansion Reborn 1.0.2B, ProductManager 2.4, RV Repair Van 2.6.2, S1API (Forked by Bars)
3.2.1-beta.7, S1APILoader 2.5.0, Seasoned Hustler 1.3.7, Siesta 1.2.3, Smart Chemists 1.0.0, Smart
Handlers 1.0.0, Smart Slot Filter 1.1.0, SmartRestock 1.3.3, StackPro 1.1.1, TightBeam 2.1.1, Trash
Recycler 1.0.0, Worker Collision Reborn 1.0.0.

**Also run with earlier builds of S1UMF** (on 0.4.7f6), since removed from this setup by choice
rather than for problems: Advanced Dealer 1.4.9, Auto Sprinkler And Soil Pourer, BankingApp 1.3.1, Custom
Commands Framework 1.1.3, Enhanced ATM 1.0.2, FasterDealers 3.0.0, Graffiti Unlimited 1.1.4, Hire Me
2.2.1, Keybind Manager 1.1.0, MyPhone 1.1, Phone Scroll 1.4, Phone Wallpaper 1.1.4, Simple Call 1.2.2,
Simple Labels 2.2.2, Sleep Anytime 1.1, Spraypaint 1.0.6, Warehouse Always Open 1.0.2.6b, Yoink 1.2.1.

Windows should behave the same; the bugs are in mod logic, not the platform. Reports welcome.

## Notes for the mod authors

If you maintain one of these mods: the cause and fix above are yours to take, in whatever form suits your
code - and once your release is out, this repo's fix stands down on its own. An issue or message if you'd
rather it did something else is welcome.

One thing worth knowing for anyone writing fixes like these: never find another mod's types with
`AccessTools.TypeByName`. It walks every loaded assembly, and `GetTypes()` on an IL2CPP interop assembly can
end the process with a fatal CLR error (`0x80131506`) that no `try`/`catch` sees. S1UMF looks each target
up in that mod's own assembly via `MelonBase.RegisteredMelons`.

## Thanks - standing on the shoulders of giants

S1UMF is a few hundred lines of patches. Everything it patches, and everything it stands on, is other
people's work, most of it given away for free. Thank you.

**DooDesch**, above all. [Polyfill](https://github.com/DooDesch-Mods/ScheduleOne-Polyfill) is what makes an
old mod run on a new Schedule I at all. Its design, its 0.4.7 analysis and the notes in its source are what
this work leans on throughout, and DooDesch reviewed, tested and merged the fixes that moved there from S1UMF.
DooDesch also made six of the mods tested here: Personnel, RV Repair Van,
Siesta, TightBeam, Yoink - and Polyfill itself.

**The authors of every mod S1UMF was tested with.** The bugs S1UMF works around are the kind any mod hits
when a game changes under it; the mods are the point, and they are good:

- 9ate7six - Absorbent Soil
- Alduin / AlduinFeynDoJun - Expanded Storage Reborn, Production Expansion Reborn, Worker Collision Reborn
- Bars (ifBars) - Bars Graphics, Drug Expansion, Forklift & Pallets, MLVScan, the S1API fork and S1APILoader
- CoolCraftBuilds - Smart Chemists, Smart Handlers, Smart Slot Filter
- Cubandsweety - Custom Commands Framework, Enhanced ATM, Phone Wallpaper
- D-Kay and contributors - Inventory Expanded - Backpack & Inventory QoL
- DazUki - Laundry App
- DerTomDerTwitch (Lithium) and its fork maintainer - Lithium (fork)
- DropDaDeuce - K9 Patrol
- ElioWasTaken - Warehouse Always Open
- Fadestyle and Virtunerd - The Big Pimpin
- HazDS - AutoReorder, BankingApp
- hdlmrell - Over The Counter, OTC Loader, MeshVault
- ionutbuzz - All Your Clients Will Order
- Jack Herer - HererMiniMap
- j0ckinjz - ExperienceBar
- k073l - BusinessEmployment, EmployeeTweaks, FurnitureDelivery
- KaBooMa - S1API, S1APILoader
- Kaen01 - EZInventory
- ManZune (original) and Virtunerd - Advanced Dealer
- manjaroman2 - Better Products Page
- Overlord970 - Dealer Routes
- OverweightUnicorn / UnicornsCanMod - Better Counter-Offer UI, Hire Me
- Prowiler - Mod Manager & Phone App, Auto Clear Completed Deals
- Pyrex - Better Customer List
- RehabVro and immaBeginner - Seasoned Hustler
- riccaforte - FasterDealers, Keybind Manager
- robbmanes, MethodNotAllowed and Owryn - ExpandRecipe
- Shaklin - CityCats, ColoredDeals
- ShunPax - Delivery Control
- SirUncleTyrone and Voidane - More Guns Forked
- SovaBFG - BFG Better Supplier, BFG Smart Deal Location
- Syke98 - MapRegionNames
- TheHypnoticFox - Cartel Influence Enhancements
- ThrustGoblin - POI Compass Markers, Spraypaint, and Drones (forked by Virtunerd)
- tiagovito - Simple Call, Simple Labels
- Ultroman The Tacoman - More Save Backups, Auto Sprinkler And Soil Pourer
- UncleTyrone - Police Response Overhaul
- uplusion23 - SmartRestock
- V4LEXL - eMployee, Graffiti Unlimited, MyPhone, NetEye, Phone Scroll, Pocket Shop, ProductManager, StackPro
- Virtunerd - BusinessTracker
- Wxki - Bargaining Assistant, Sleep Anytime, Trash Recycler, and the Spraypaint update

**The ground it all stands on:** Schedule I by **TVGS**. [MelonLoader](https://github.com/LavaGang/MelonLoader)
by **LavaGang**. [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop) by the **BepInEx** team.
[Harmony](https://github.com/pardeike/Harmony) by **Andreas Pardeike**, and **MonoMod** under it.
[Mono.Cecil](https://github.com/jbevain/cecil) by **Jb Evain**. [Cpp2IL](https://github.com/SamboyCoding/Cpp2IL)
by **Samboy063**, whose address table made the folded-code sweep possible. **Ghidra** and ILSpy for reading
what the game does.

If your name is missing or wrong, or you would rather a fix for your mod were done differently, open an
issue - it will be changed. Diagnosed and written with Claude (Anthropic).

## License

MIT - see [LICENSE](LICENSE). It covers this repo's code only; the mods it patches belong to their authors.
