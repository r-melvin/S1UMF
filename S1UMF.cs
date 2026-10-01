using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using System.Collections;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.Equipping;
using MelonLoader;
using UnityEngine;
using UnityEngine.AI;

[assembly: MelonInfo(typeof(S1UMF.Mod), "S1UMF", "0.1.0", "r-melvin")]
[assembly: MelonGame("TVGS", "Schedule I")]
// Initialise before other mods: one fix has to be in place before EmployeeTweaks applies its patches.
[assembly: MelonPriority(-1000)]

namespace S1UMF
{
    /// <summary>
    /// Fixes for bugs in other mods on Schedule I 0.4.7f6 that are not missing-member gaps (those go to
    /// Polyfill). Each fix names the exact mod version it was written against and stands down otherwise,
    /// so an upstream release that fixes the bug switches this off by itself.
    /// </summary>
    /// <remarks>
    /// Mod types are looked up in that mod's own assembly, never with AccessTools.TypeByName: that walks
    /// every loaded assembly, and GetTypes() on an IL2CPP interop assembly can kill the process with a
    /// fatal CLR error (0x80131506) that no try/catch sees.
    /// </remarks>
    public class Mod : MelonMod
    {
        // Fixes that must be in place before other mods initialise run here; the rest run late, once every
        // mod has registered.
        public override void OnInitializeMelon()
        {
            ApplyEmployeeTweaks();
        }

        public override void OnLateInitializeMelon()
        {
            Apply("K9 Patrol", "1.1.0", "K9_Patrol.Source.NPCs.K9NPC", "RequestPath",
                  nameof(K9RequestPath), "dogs stuck with a path that never finishes computing");
            Apply("MoreGuns", "1.6.6", "MoreGuns.Patches.Equippalbe_RangedWeaponPatch", "Postfix",
                  nameof(MoreGunsAutoFire), "automatic fire from a left click still held when the gun came out");
            ApplyIntegerItemUI();
            ApplyCustomerSelector();
            ApplyTypeSweepGuards();
            Apply("Better Products Page", "1.1", "BetterProductsPage.BetterProductsPage+ProductManagerApp_SelectProduct_Patch",
                  "Postfix", nameof(BetterProductsPaths), "selecting a product made after the save loaded throws");
            Apply("Police Response Overhaul", "1.1.6", "PoliceResponseOverhaul.Patches.PursuitWeaponChangedGuardPatch",
                  "Prefix", nameof(PoliceBeltGuard), "officers' weapon changes skipped (the belt moved on 0.4.7)");
            Apply("HererMiniMap", "2.0.1", "HererMiniMap.MinimapSettingsApp", "Create",
                  nameof(MinimapWithoutPhone), "a warning every 2 seconds at the main menu, where there is no phone");
            ApplyHererMinimapLayering();
            ApplyBigPimpin();
            ApplyTemplateNpcId();
            RequirePolyfill();
#if S1UMF_DEV
            ApplyDebugSkips();
            if (!_profStarted) { _profStarted = true; MelonCoroutines.Start(ProfLog()); }
#endif
#if S1UMF_DEV
            DumpPatches("after mod init");
#endif
        }

        // ---------------------------------------------------------------- HererMiniMap 2.0.1: layering (a tweak, not a bug fix)
        //
        // Its canvas is a screen-space overlay at sorting order 1000, shown from the moment it is built: it appears
        // over the loading screen (order 200), and stays over the pause menu (100), tooltips (110) and the rest of
        // the game's UI (all 1-35). Put it under the game's UI (-2: below even the phone and HUD, -1 and 1), and
        // show it only while a game is loaded and the loading screen is closed.

        private const string HererCanvas = "Herer_MinimapCanvas";
        private const int HererBehindUi = -2;

        private void ApplyHererMinimapLayering()
        {
            const string what = "the minimap shows over the loading screen and on top of menus and the phone";
            if (Gate("HererMiniMap", "2.0.1", what) == null) return;
            MelonCoroutines.Start(HererLayering());
            LoggerInstance.Msg("[HererMiniMap 2.0.1] tweak: the minimap now only shows with the game world, and sits behind menus and the loading screen");
        }

        private static IEnumerator HererLayering()
        {
            Canvas canvas = null;
            float nextSearch = 0f;
            while (true)
            {
                yield return new WaitForSecondsRealtime(0.2f);
                try
                {
                    // The mod builds a new canvas for every game it loads; the old one is destroyed with its scene.
                    if (canvas == null && Time.unscaledTime >= nextSearch)
                    {
                        nextSearch = Time.unscaledTime + 1f;
                        var go = GameObject.Find(HererCanvas);
                        canvas = go != null ? go.GetComponent<Canvas>() : null;
                        if (canvas != null) canvas.sortingOrder = HererBehindUi;
                    }
                    if (canvas != null) canvas.enabled = GameWorldVisible();
                }
                catch (Exception) { canvas = null; }
            }
        }

        private static bool GameWorldVisible()
        {
            if (!Singleton<LoadManager>.InstanceExists) return false;
            var load = Singleton<LoadManager>.Instance;
            if (!load.IsGameLoaded || load.IsLoading) return false;
            return !(Singleton<LoadingScreen>.InstanceExists && Singleton<LoadingScreen>.Instance.IsOpen);
        }

        // ---------------------------------------------------------------- The Big Pimpin 1.0.11
        //
        // Two of its patches are written against 0.4.6 signatures Harmony cannot bind on 0.4.7:
        //  - Patch_HandoverScreen_Open prefixes Open(contract, customer, mode, callback): an escort ("date")
        //    contract submits an empty handover through the callback and runs the mod's escort handover instead
        //    of showing the product screen. 0.4.7 opens a contract handover with Open_Contract(contract,
        //    onSubmit(items), onCancel, ...), and the customer is the contract's. Do the same there.
        //  - Patch_MessagesApp_CreateConversationUI names the conversation "c"; 0.4.7 calls it "conversation" and
        //    passes entry and container by reference. Call its postfix with them, so the pimp's icon shows.

        private static MethodInfo _bpContainsEscort, _bpRunEscort, _bpIconPostfix;

        private void ApplyBigPimpin()
        {
            const string what = "escort contracts open the product handover, and the pimp's messages icon is missing (0.4.7 signatures)";
            var melon = Gate("BigPimpin", "1.0.11", what);
            if (melon == null) return;
            try
            {
                var asm = melon.MelonAssembly.Assembly;
                var escort = asm.GetType("bigpimpin.Patches.EscortContractPatches", false);
                _bpContainsEscort = escort == null ? null : AccessTools.Method(escort, "ContainsEscortProductPublic");
                _bpRunEscort = escort == null ? null : AccessTools.Method(escort, "RunEscortHandover");
                var icon = asm.GetType("bigpimpin.Messaging.MessagesAppIconPatch+Patch_MessagesApp_CreateConversationUI", false);
                _bpIconPostfix = icon == null ? null : AccessTools.Method(icon, "Postfix");

                int applied = 0;
                var open = AccessTools.Method(typeof(Il2CppScheduleOne.UI.Handover.HandoverScreen), "Open_Contract");
                if (open != null && _bpContainsEscort != null && _bpRunEscort != null)
                {
                    HarmonyInstance.Patch(open, prefix: new HarmonyMethod(typeof(Mod), nameof(EscortHandover)));
                    applied++;
                }
                var create = AccessTools.Method(typeof(Il2CppScheduleOne.UI.Phone.Messages.MessagesApp), "CreateConversationUI");
                if (create != null && _bpIconPostfix != null)
                {
                    HarmonyInstance.Patch(create, postfix: new HarmonyMethod(typeof(Mod), nameof(PimpIcon)));
                    applied++;
                }
                if (applied == 2) LoggerInstance.Msg($"[BigPimpin 1.0.11] fixed: {what}");
                else LoggerInstance.Warning($"[BigPimpin] only {applied} of 2 parts of the fix for {what} found their targets");
            }
            catch (Exception e)
            {
                LoggerInstance.Warning($"[BigPimpin] could not apply fix for {what}: {e.Message}");
            }
        }

        private static bool EscortHandover(Il2CppScheduleOne.Quests.Contract contract,
            Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance>> onSubmitCallback)
        {
            try
            {
                if (contract == null || !(bool)_bpContainsEscort.Invoke(null, new object[] { contract.ProductList })) return true;
                var customer = contract.Customer == null ? null : contract.Customer.GetComponent<Il2CppScheduleOne.Economy.Customer>();
                if (customer == null) return true;
                try { onSubmitCallback?.Invoke(new Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance>()); }
                catch (Exception e) { MelonLogger.Warning("[S1UMF] Big Pimpin escort handover: submit failed: " + e.Message); }
                _bpRunEscort.Invoke(null, new object[] { contract, customer });
                return false;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[S1UMF] Big Pimpin escort handover: " + (e.InnerException?.Message ?? e.Message));
                return true;
            }
        }

        private static void PimpIcon(Il2CppScheduleOne.Messaging.MSGConversation conversation,
                                     ref RectTransform entry, ref RectTransform container)
        {
            try { _bpIconPostfix.Invoke(null, new object[] { conversation, entry, container }); }
            catch { }
        }

        // ---------------------------------------------------------------- Game 0.4.7f6-f7: NPC ids at load
        //
        // NPC.ID reads NPCData.BasicInfo.ID in play mode without a null check (the editor branch has one). The
        // game's own pooled special customers (SpecialCustomerManager.SetNPCs prewarms them, InitializeNPC
        // gives them data only when a group arrives) have none, and so do custom NPC templates. Asking one of
        // them for its id throws - seen from the load routine and from mods walking the NPCs when the loading
        // screen closes (Over The Counter, S1API, SmartRestock). Answer "" for an NPC with no data, as the
        // game's own getter does outside play mode; any other exception still throws.

        private static int _templateIds;

        private void ApplyTemplateNpcId()
        {
            const string what = "a custom NPC template has no NPC data, and asking for its id throws at load";
            if (!GameFits())
            {
                LoggerInstance.Msg($"[game] is {Application.version}, fix for {what} was written for 0.4.7f6-f7 - standing down");
                return;
            }
            try
            {
                var target = AccessTools.PropertyGetter(typeof(Il2CppScheduleOne.NPCs.NPC), "ID");
                if (target == null) { LoggerInstance.Warning($"[game] NPC.ID not found - fix for {what} not applied"); return; }
                HarmonyInstance.Patch(target, finalizer: new HarmonyMethod(typeof(Mod), nameof(TemplateNpcId)));
                LoggerInstance.Msg($"[game {Application.version}] fixed: {what}");
            }
            catch (Exception e)
            {
                LoggerInstance.Warning($"[game] could not apply fix for {what}: {e.Message}");
            }
        }

        private static Exception TemplateNpcId(Exception __exception, Il2CppScheduleOne.NPCs.NPC __instance, ref string __result)
        {
#if S1UMF_DEV
            long __t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try {
#endif
            if (__exception == null) return null;
            try
            {
                // The one case this answers: an NPC the game never gave data to (ID reads NPCData.BasicInfo.ID).
                if (__instance == null || __instance.HasNPCData) return __exception;
            }
            catch { return __exception; }
            if (_templateIds++ < 5)
            {
                string path = "?";
                try
                {
                    var parts = new System.Collections.Generic.List<string>();
                    for (var t = __instance.transform; t != null; t = t.parent) parts.Insert(0, t.name);
                    path = string.Join("/", parts) + (__instance.gameObject.activeInHierarchy ? "" : " (inactive)");
                }
                catch { }
                MelonLogger.Msg($"[S1UMF] NPC {path} has no NPC data, so no id - answered \"\" (as the game does outside play mode)");
            }
            __result = string.Empty;
            return null;
#if S1UMF_DEV
            } finally { System.Threading.Interlocked.Increment(ref ProfCalls[2]); System.Threading.Interlocked.Add(ref ProfTicks[2], System.Diagnostics.Stopwatch.GetTimestamp() - __t0); }
#endif
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
#if S1UMF_DEV
            if (sceneName == "Main") MelonCoroutines.Start(DumpLater());
            if (!_autoloadStarted && sceneName == "Menu") StartAutoLoad();
#endif
        }

#if S1UMF_DEV
        // Hot-path counters: calls and time spent per hook, logged every 30 s. Developer build only.
        internal static readonly string[] ProfNames = { "IntegerItemUI", "K9RequestPath", "NpcIdFinalizer" };
        internal static readonly long[] ProfCalls = new long[3], ProfTicks = new long[3];
        private static bool _profStarted;
        private static System.Collections.IEnumerator ProfLog()
        {
            while (true)
            {
                yield return new WaitForSeconds(30f);
                var parts = new System.Collections.Generic.List<string>();
                for (int i = 0; i < ProfNames.Length; i++)
                {
                    long c = System.Threading.Interlocked.Exchange(ref ProfCalls[i], 0), t = System.Threading.Interlocked.Exchange(ref ProfTicks[i], 0);
                    double us = c == 0 ? 0 : t * 1e6 / System.Diagnostics.Stopwatch.Frequency;
                    parts.Add($"{ProfNames[i]} {c} calls {us:F0}us ({(c == 0 ? 0 : us * 1000 / c):F0}ns/call)");
                }
                MelonLogger.Msg("[prof] 30s: " + string.Join(" | ", parts));
            }
        }
#endif

#if S1UMF_DEV
        // ================================================================ developer build only (-p:Dev=true)
        // Switches for chasing crashes. Not in the release build, so a stray file cannot change a player's game.

        // ---------------------------------------------------------------- debugging: load a save unattended
        //
        // Only when UserData/S1UMF.autoload exists (its text: the save slot, 1-5; default 1). At the main menu
        // the save is started as the Continue screen would. For reproducing a crash that needs a save to load,
        // launch after launch, without anyone clicking. Delete the file to switch it off.

        private bool _autoloadStarted;

        private void StartAutoLoad()
        {
            var marker = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "S1UMF.autoload");
            if (!System.IO.File.Exists(marker)) return;
            int slot = 1;
            try { int.TryParse(System.IO.File.ReadAllText(marker).Trim(), out slot); } catch { }
            if (slot < 1 || slot > 5) slot = 1;
            _autoloadStarted = true;
            MelonCoroutines.Start(AutoLoad(slot - 1));
        }

        private System.Collections.IEnumerator AutoLoad(int index)
        {
            for (float waited = 0f; waited < 60f; waited += 1f)
            {
                yield return new WaitForSeconds(1f);
                var saves = Il2CppScheduleOne.Persistence.LoadManager.SaveGames;
                var info = saves != null && saves.Length > index ? saves[index] : null;
                if (info == null || !Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Persistence.LoadManager>.InstanceExists)
                    continue;
                yield return new WaitForSeconds(3f);
                LoggerInstance.Msg($"[autoload] starting save slot {index + 1} (UserData/S1UMF.autoload)");
                Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Persistence.LoadManager>.Instance.StartGame(info);
                yield break;
            }
            LoggerInstance.Warning($"[autoload] save slot {index + 1} never appeared - not loading");
        }

        // ---------------------------------------------------------------- debugging: neutralise mod methods
        //
        // Only when UserData/S1UMF.skip exists. Each line "Assembly name|Namespace.Type|Method" makes that
        // method of that mod do nothing (a void method returns at once; anything else returns its default).
        // For narrowing a crash down to one patch of one mod. Delete the file to switch it off.

        private void ApplyDebugSkips()
        {
            var file = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "S1UMF.skip");
            if (!System.IO.File.Exists(file)) return;
            foreach (var raw in System.IO.File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split('|');
                if (parts.Length != 3) { LoggerInstance.Warning($"[skip] not Assembly|Type|Method: {line}"); continue; }
                try
                {
                    var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == parts[0]);
                    var type = asm?.GetType(parts[1], false);
                    var methods = type?.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                                                   | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                                       .Where(m => m.Name == parts[2]).ToList();
                    if (methods == null || methods.Count == 0) { LoggerInstance.Warning($"[skip] not found: {line}"); continue; }
                    foreach (var m in methods)
                        HarmonyInstance.Patch(m, prefix: new HarmonyMethod(typeof(Mod), nameof(SkipMethod)));
                    LoggerInstance.Warning($"[skip] DEBUG: {line} does nothing this session ({methods.Count} overload(s))");
                }
                catch (Exception e) { LoggerInstance.Warning($"[skip] {line}: {e.Message}"); }
            }
        }

        private static bool SkipMethod() => false;

        // ---------------------------------------------------------------- debugging: dump patched game methods
        //
        // Only when UserData/S1UMF.dumppatches exists. Writes UserData/S1UMF.patches.tsv: every Harmony-patched
        // method in the game's interop, its owners, and its native entry as an RVA into GameAssembly.dll - so
        // patches on code IL2CPP folded with other methods can be found offline.

        private System.Collections.IEnumerator DumpLater()
        {
            yield return new WaitForSeconds(30f);
            DumpPatches("after the save loaded");
        }

        private void DumpPatches(string when)
        {
            var dir = MelonLoader.Utils.MelonEnvironment.UserDataDirectory;
            if (!System.IO.File.Exists(System.IO.Path.Combine(dir, "S1UMF.dumppatches"))) return;
            try
            {
                long gameAssembly = 0;
                foreach (System.Diagnostics.ProcessModule m in System.Diagnostics.Process.GetCurrentProcess().Modules)
                    if (string.Equals(m.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase)) gameAssembly = (long)m.BaseAddress;
                var lines = new System.Collections.Generic.List<string> { "# " + when + "\ttype\tmethod\tparams\trva\towners" };
                foreach (var method in HarmonyLib.Harmony.GetAllPatchedMethods())
                {
                    string rva = "";
                    try
                    {
                        var field = Il2CppInterop.Common.Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
                        if (field != null)
                        {
                            var info = (IntPtr)field.GetValue(null);
                            if (info != IntPtr.Zero)
                            {
                                long code = (long)System.Runtime.InteropServices.Marshal.ReadIntPtr(info);
                                rva = gameAssembly != 0 && code > gameAssembly ? "0x" + (code - gameAssembly).ToString("X") : "abs:0x" + code.ToString("X");
                            }
                        }
                        else rva = "managed";
                    }
                    catch (Exception e) { rva = "err:" + e.GetType().Name; }
                    var info2 = HarmonyLib.Harmony.GetPatchInfo(method);
                    var owners = info2 == null ? "" : string.Join(",", info2.Owners);
                    var pars = string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name));
                    lines.Add($"{when}\t{method.DeclaringType?.FullName}\t{method.Name}\t{pars}\t{rva}\t{owners}");
                }
                System.IO.File.AppendAllLines(System.IO.Path.Combine(dir, "S1UMF.patches.tsv"), lines);
                LoggerInstance.Msg($"[dump] {lines.Count - 1} patched methods written ({when})");
            }
            catch (Exception e) { LoggerInstance.Warning("[dump] " + e.Message); }
        }

#endif

        // ---------------------------------------------------------------- Polyfill
        //
        // S1UMF fixes what is broken inside other mods. What the 0.4.7 update renamed or removed, and the three
        // gaps that used to be fixed here - patches on code IL2CPP folded with other classes, Expanded Storage's
        // bulk patch targets and handover screen, Production Expansion Reborn's trash bags - are Polyfill's, so
        // those fixes are not repeated. They need a Polyfill that has them: say so when it does not.

        private static readonly string[] PolyfillNeeds =
        {
            "Polyfill.ModFixes.PatchesOnFoldedCode",
            "Polyfill.ModFixes.PatchesOnDroppedArguments",
            "Polyfill.Boot.BulkTargetsSkipBridges",
        };

        private static bool PolyfillHas(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = assembly.GetName().Name;
                if (name != "Polyfill" && name != "Polyfill.Boot") continue;
                try { if (assembly.GetType(typeName, false) != null) return true; } catch { }
            }
            return false;
        }

        private void RequirePolyfill()
        {
            var polyfill = MelonBase.RegisteredMelons.FirstOrDefault(m => m.Info.Name == "Polyfill");
            if (polyfill == null)
            {
                LoggerInstance.Warning("Polyfill is not installed. Many mods here were written for 0.4.6 and need it on 0.4.7: "
                                       + "install Polyfill 0.13.0 or newer from https://www.nexusmods.com/schedule1/mods/2452");
                return;
            }
            var missing = PolyfillNeeds.Where(t => !PolyfillHas(t)).Select(t => t.Substring(t.LastIndexOf('.') + 1)).ToList();
            if (missing.Count == 0)
                LoggerInstance.Msg($"[Polyfill {polyfill.Info.Version}] has the fixes S1UMF relies on");
            else
                LoggerInstance.Warning($"Polyfill {polyfill.Info.Version} is older than S1UMF needs (missing {string.Join(", ", missing)}): "
                                       + "update to Polyfill 0.13.0 or newer from https://www.nexusmods.com/schedule1/mods/2452");
        }

        // ---------------------------------------------------------------- Better Products Page 1.1
        //
        // Its SelectProduct postfix reads the recipe paths of the selected product from a map it fills once, when
        // a save loads (ProductManagerLoader.Load). A product mixed or added after that has no entry, so selecting
        // it throws KeyNotFoundException - 111 times in one evening. Fill the missing entry with the mod's own
        // builder first; the page then shows that product's recipes as it does for the others.

        private static FieldInfo _bppPaths;
        private static MethodInfo _bppBuild;

        private static void BetterProductsPaths(Il2CppScheduleOne.Product.ProductEntry entry)
        {
            try
            {
                var definition = entry?.Definition;
                if (definition == null) return;
                if (_bppPaths == null)
                {
                    var asm = MelonBase.RegisteredMelons.First(m => m.Info.Name == "Better Products Page").MelonAssembly.Assembly;
                    _bppPaths = AccessTools.Field(asm.GetType("BetterProductsPage.BetterProductsPage"), "_defintionProductPaths");
                    _bppBuild = AccessTools.Method(asm.GetType("BetterProductsPage.ProductPath"), "buildProductPathsCached");
                }
                if (!(_bppPaths?.GetValue(null) is System.Collections.IDictionary paths) || _bppBuild == null) return;
                if (paths.Contains(definition.GetInstanceID())) return;
                _bppBuild.Invoke(null, new object[] { definition, new System.Collections.Generic.HashSet<int>(), paths });
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[S1UMF] Better Products Page recipe paths: " + (e.InnerException?.Message ?? e.Message));
            }
        }

        // ---------------------------------------------------------------- Police Response Overhaul 1.1.6
        //
        // Its guard on PursuitBehaviour.OnCurrentWeaponChanged lets the game's method run only when the officer has
        // a belt, filling PoliceOfficer.belt from the avatar when it is empty. 0.4.7 has no such field: the officer
        // keeps a ScheduleOne.Law.PoliceBelt in PoliceBelt, found in Awake, and the AvatarFramework.PoliceBelt the
        // guard looks for is no longer on an officer - so the guard would skip every weapon change. Ask the
        // question of the belt 0.4.7 has. (The mod's own prefix takes the behaviour as its first argument,
        // named __instance, which Harmony reserves - so it is read here as __0.)

        private static bool PoliceBeltGuard(Il2CppScheduleOne.NPCs.Behaviour.PursuitBehaviour __0, ref bool __result)
        {
            __result = false;
            try
            {
                var officer = __0 == null ? null : __0.officer;
                if (officer == null) return false;
                if (officer.PoliceBelt == null && officer.Avatar != null)
                    officer.PoliceBelt = officer.Avatar.GetComponentInChildren<Il2CppScheduleOne.Law.PoliceBelt>(true);
                __result = officer.PoliceBelt != null;
            }
            catch { }
            return false;
        }

        // ---------------------------------------------------------------- HererMiniMap 2.0.1
        //
        // A watchdog re-creates the phone app every 2 seconds, and at the main menu - where there is no phone - it
        // logs "AppIcons container not found" each time. Answer "not created" quietly until a phone exists.

        private static bool MinimapWithoutPhone(ref bool __result)
        {
            if (Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.UI.Phone.HomeScreen>.InstanceExists) return true;
            __result = false;
            return false;
        }

        // ---------------------------------------------------------------- Game 0.4.7f6: CustomerSelector
        //
        // The dealer app's "assign customer" list hides customers who already have a dealer, shows itself, then
        // selects the first visible entry: customerEntries.FirstOrDefault(x => x.activeSelf).GetComponent(...)
        // (CustomerSelector.cs:57-72 on 0.4.7f6). With every customer assigned there is no visible entry and the
        // selection throws after the list is already open. Contain that one exception; the list is shown, empty.

        private static int _selectorContained;

        private void ApplyCustomerSelector()
        {
            const string what = "the assign-customer list throws when every customer already has a dealer (0.4.7f6)";
            if (!GameFits())
            {
                LoggerInstance.Msg($"[game] is {Application.version}, fix for {what} was written for 0.4.7f6-f7 - standing down");
                return;
            }
            try
            {
                var target = AccessTools.Method(typeof(Il2CppScheduleOne.UI.Phone.CustomerSelector), "Open");
                if (target == null) { LoggerInstance.Warning($"[game] CustomerSelector.Open not found - fix for {what} not applied"); return; }
                HarmonyInstance.Patch(target, finalizer: new HarmonyMethod(typeof(Mod), nameof(ContainCustomerSelector)));
                LoggerInstance.Msg($"[game {Application.version}] fixed: {what}");
            }
            catch (Exception e)
            {
                LoggerInstance.Warning($"[game] could not apply fix for {what}: {e.Message}");
            }
        }

        private static Exception ContainCustomerSelector(Exception __exception, Il2CppScheduleOne.UI.Phone.CustomerSelector __instance)
        {
            if (__exception == null) return null;
            try
            {
                // Only the selection step, which runs after the list is shown; anything earlier is not ours to hide.
                if (__instance == null || !__instance.gameObject.activeSelf) return __exception;
            }
            catch { return __exception; }
            if (_selectorContained++ < 3)
                MelonLogger.Msg("[S1UMF] assign-customer list opened with no unassigned customer to select");
            return null;
        }

        // Game builds the game fixes were checked against. Each one guards or contains a specific failure, so on a
        // build that fixed it the guard simply never fires; a build not listed here stands them all down.
        private static readonly string[] GameBuilds = { "0.4.7f6", "0.4.7f7" };

        private static bool GameFits() => Array.IndexOf(GameBuilds, Application.version) >= 0;

        private MelonBase Gate(string melonName, string version, string what)
        {
            var melon = MelonBase.RegisteredMelons.FirstOrDefault(m => m.Info.Name == melonName);
            if (melon == null)
            {
                LoggerInstance.Msg($"[{melonName}] not installed - fix for {what} not needed");
                return null;
            }
            if (melon.Info.Version != version)
            {
                LoggerInstance.Msg($"[{melonName}] is {melon.Info.Version}, fix was written for {version} - standing down");
                return null;
            }
            return melon;
        }

        private void Apply(string melonName, string version, string typeName, string method, string fix, string what)
        {
            var melon = Gate(melonName, version, what);
            if (melon == null) return;
            try
            {
                var type = melon.MelonAssembly.Assembly.GetType(typeName, throwOnError: false);
                var target = type == null ? null : AccessTools.Method(type, method);
                if (target == null)
                {
                    LoggerInstance.Warning($"[{melonName}] {typeName}.{method} not found - fix for {what} not applied");
                    return;
                }
                HarmonyInstance.Patch(target, prefix: new HarmonyMethod(typeof(Mod), fix));
                LoggerInstance.Msg($"[{melonName} {version}] fixed: {what}");
            }
            catch (Exception e)
            {
                LoggerInstance.Warning($"[{melonName}] could not apply fix for {what}: {e.Message}");
            }
        }

        // ---------------------------------------------------------------- EmployeeTweaks 1.0.10
        //
        // PropertyPatch.ManualPatchProperties puts a CatchLate postfix on the Awake of Property and four of its
        // subclasses. On 0.4.7f6 Bungalow.Awake and SewerOffice.Awake have identical bodies (NetworkInitialize___
        // Early; base.Awake(); NetworkInitialize__Late), and IL2CPP folded them into ONE native function - the
        // interop shows SewerOffice.Awake with no native body of its own (xref range 0-0). Harmony detours the same
        // native address twice, each detour's "original" is the other, and the first Bungalow or Sewer Office to
        // wake during a load overflows the main thread's stack: the save never finishes loading. Run the same
        // patching, once per native function.

        private static Type _etPropertyPatch;

        private void ApplyEmployeeTweaks()
        {
            const string what = "stack overflow loading a save (two Awake methods share one native function)";
            var melon = Gate("EmployeeTweaks", "1.0.10", what);
            if (melon == null) return;
            try
            {
                _etPropertyPatch = melon.MelonAssembly.Assembly.GetType("EmployeeTweaks.Patches.EmployeeArea.PropertyPatch", false);
                var target = _etPropertyPatch == null ? null : AccessTools.Method(_etPropertyPatch, "ManualPatchProperties");
                if (target == null) { LoggerInstance.Warning($"[EmployeeTweaks] ManualPatchProperties not found - fix for {what} not applied"); return; }
                HarmonyInstance.Patch(target, prefix: new HarmonyMethod(typeof(Mod), nameof(PatchPropertiesOnce)));
                LoggerInstance.Msg($"[EmployeeTweaks 1.0.10] fixed: {what}");
            }
            catch (Exception e)
            {
                LoggerInstance.Warning($"[EmployeeTweaks] could not apply fix for {what}: {e.Message}");
            }
        }

        private static bool PatchPropertiesOnce(HarmonyLib.Harmony harmony)
        {
            try
            {
                var catchLate = AccessTools.Method(_etPropertyPatch, "CatchLate");
                if (catchLate == null) return true;                              // unknown shape: leave it to the mod
                var seen = new System.Collections.Generic.HashSet<IntPtr>();
                foreach (var type in new[]
                {
                    typeof(Il2CppScheduleOne.Property.Bungalow), typeof(Il2CppScheduleOne.Property.Manor),
                    typeof(Il2CppScheduleOne.Property.SewerOffice), typeof(Il2CppScheduleOne.Property.Business),
                    typeof(Il2CppScheduleOne.Property.Property),
                })
                {
                    var awake = type.GetMethod("Awake", BindingFlags.DeclaredOnly | BindingFlags.Instance
                                                        | BindingFlags.Public | BindingFlags.NonPublic);
                    if (awake == null) continue;
                    var native = NativeCode(type, "Awake");
                    if (native != IntPtr.Zero && !seen.Add(native))
                    {
                        MelonLogger.Msg($"[S1UMF] EmployeeTweaks: {type.Name}.Awake shares native code with one already patched - not patched twice");
                        continue;
                    }
                    harmony.Patch(awake, postfix: new HarmonyMethod(catchLate));
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[S1UMF] EmployeeTweaks property patching: " + e.Message);
            }
            return false;
        }

        /// <summary>The native code address behind an interop method, read from its Il2CppMethodInfo.</summary>
        private static IntPtr NativeCode(Type type, string method)
        {
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                if (field.FieldType == typeof(IntPtr) && field.Name.StartsWith("NativeMethodInfoPtr_" + method + "_", StringComparison.Ordinal))
                {
                    var info = (IntPtr)field.GetValue(null);
                    // Il2CppMethodInfo begins with methodPointer.
                    return info == IntPtr.Zero ? IntPtr.Zero : System.Runtime.InteropServices.Marshal.ReadIntPtr(info);
                }
            return IntPtr.Zero;
        }

        // ---------------------------------------------------------------- GetTypes guards (V4LEXL's phone mods)
        //
        // PhoneScroll, NetEye and ProductManager find game types by walking AppDomain.GetAssemblies() and
        // calling GetTypes() on every one - interop assemblies included. That can end the process with a fatal
        // CLR error (0x80131506) no try/catch sees; measured as a crash right after a save loaded, while phone
        // apps were being built. Every one of those walks has a single, known answer on 0.4.7f6, so it is
        // answered up front and the walk never runs:
        //   PhoneScroll.ResolveHomeScreenType()      -> Il2CppScheduleOne.UI.Phone.HomeScreen
        //   NetEye/ProductManager TryFindPhone        -> _phoneType = Il2CppScheduleOne.UI.Phone.Phone (the only
        //                                                type with "Phone" in its name and RequestCloseApp)
        //   ProductManager.ResolveGameSingletons      -> IconGenerator, ProductIconManager, ScheduleOne.Registry
        //   ProductManager.AppearanceTypeForName(name) -> looked up by name in the game assembly's METADATA, which
        //                                                 reads a table and loads no type.

        private static readonly Type PhoneType = typeof(Il2CppScheduleOne.UI.Phone.Phone);

        private void ApplyTypeSweepGuards()
        {
            const string what = "a GetTypes() walk over the game's interop assemblies (fatal 0x80131506 risk)";
            Guard("PhoneScroll", "1.4", "PhoneScroll.PhoneScrollMod", "ResolveHomeScreenType", nameof(HomeScreenType), what);
            Guard("NetEye", "1.1.0", "NetEye.NetEyeMod", "TryFindPhone", nameof(PresetPhoneType), what);
            Guard("ProductManager", "2.4", "ProductManager.ProductManagerMod", "TryFindPhone", nameof(PresetPhoneType), what);
            Guard("ProductManager", "2.4", "ProductManager.ProductManagerMod", "ResolveGameSingletons", nameof(ResolveSingletons), what);
            Guard("ProductManager", "2.4", "ProductManager.ProductManagerMod", "AppearanceTypeForName", nameof(AppearanceType), what);
        }

        private void Guard(string melonName, string version, string typeName, string method, string fix, string what)
        {
            var melon = Gate(melonName, version, what);
            if (melon == null) return;
            try
            {
                var type = melon.MelonAssembly.Assembly.GetType(typeName, false);
                var target = type == null ? null : AccessTools.Method(type, method);
                if (target == null) { LoggerInstance.Warning($"[{melonName}] {method} not found - not guarded"); return; }
                HarmonyInstance.Patch(target, prefix: new HarmonyMethod(typeof(Mod), fix));
                LoggerInstance.Msg($"[{melonName} {version}] guarded {method}: {what}");
            }
            catch (Exception e)
            {
                LoggerInstance.Warning($"[{melonName}] could not guard {method}: {e.Message}");
            }
        }

        private static bool HomeScreenType(ref Type __result)
        {
            __result = typeof(Il2CppScheduleOne.UI.Phone.HomeScreen);
            return false;
        }

        // Runs the mod's own method with the answer already in place, so its `if (_phoneType == null)` walk is skipped.
        private static void PresetPhoneType(object __instance)
        {
            try
            {
                var field = AccessTools.Field(__instance.GetType(), "_phoneType");
                if (field != null && field.GetValue(__instance) == null) field.SetValue(__instance, PhoneType);
            }
            catch { }
        }

        private static bool ResolveSingletons(object __instance)
        {
            try
            {
                var t = __instance.GetType();
                if ((bool)AccessTools.Field(t, "_gameSingletonsResolved").GetValue(__instance)) return false;
                var get = AccessTools.Method(t, "TryGetSingletonInstance");
                void Fill(string field, Type game)
                {
                    var f = AccessTools.Field(t, field);
                    if (f.GetValue(__instance) == null) f.SetValue(__instance, get.Invoke(__instance, new object[] { game }));
                }
                Fill("_gameIconGeneratorInstance", typeof(Il2CppScheduleOne.DevUtilities.IconGenerator));
                Fill("_gameProductIconManagerInstance", typeof(Il2CppScheduleOne.Product.ProductIconManager));
                Fill("_gameRegistryInstance", typeof(Il2CppScheduleOne.Registry));
                AccessTools.Field(t, "_gameSingletonsResolved").SetValue(__instance,
                    AccessTools.Field(t, "_gameIconGeneratorInstance").GetValue(__instance) != null
                    && AccessTools.Field(t, "_gameProductIconManagerInstance").GetValue(__instance) != null);
            }
            catch { }
            return false;                                                     // never the walk
        }

        private static bool AppearanceType(string typeName, ref Type __result)
        {
            __result = GameTypeBySimpleName(typeName) ?? GameTypeBySimpleName("Il2Cpp" + typeName);
            return false;
        }

        private static System.Collections.Generic.Dictionary<string, string> _gameTypeNames;

        /// <summary>A game type by its simple name, found in the interop assembly's metadata; loads nothing else.</summary>
        private static Type GameTypeBySimpleName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var assembly = typeof(Il2CppScheduleOne.Registry).Assembly;
            if (_gameTypeNames == null)
            {
                var names = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    using var stream = System.IO.File.OpenRead(assembly.Location);
                    using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
                    var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
                    foreach (var handle in md.TypeDefinitions)
                    {
                        var def = md.GetTypeDefinition(handle);
                        if (!def.GetDeclaringType().IsNil) continue;               // nested: not what a name finds
                        string simple = md.GetString(def.Name), ns = md.GetString(def.Namespace);
                        names.TryAdd(simple, ns.Length == 0 ? simple : ns + "." + simple);
                    }
                }
                catch { }
                _gameTypeNames = names;
            }
            return _gameTypeNames.TryGetValue(name, out var full) ? assembly.GetType(full, false) : null;
        }

        // ---------------------------------------------------------------- Game 0.4.7f6: IntegerItemUI
        //
        // IntegerItemUI.UpdateUI is ValueLabel.text = integerItemInstance.Value; base.UpdateUI(), where
        // integerItemInstance is Setup's `item as IntegerItemInstance`. Any item whose definition borrows an
        // integer UI (MoreGuns 1.6.6 gives its guns and magazines the M1911's) but whose instance is not an
        // IntegerItemInstance throws there - inside ItemUIManager.EndDrag, StorageMenu.Open and
        // StorageEntity.SetStoredInstance - so a drag or storage refresh stops halfway: ~740 NREs in one
        // session and storage slots left showing the wrong thing. Do the same work with the value label
        // skipped when there is no value to show, and name the item once so it can be traced to its mod.

        private static int _integerUiGuarded;

        private void ApplyIntegerItemUI()
        {
            const string what = "an item with an integer UI but no integer value breaks dragging and storage (0.4.7f6)";
            if (!GameFits())
            {
                LoggerInstance.Msg($"[game] is {Application.version}, fix for {what} was written for 0.4.7f6-f7 - standing down");
                return;
            }
            try
            {
                var target = AccessTools.Method(typeof(Il2CppScheduleOne.UI.Items.IntegerItemUI), "UpdateUI");
                if (target == null) { LoggerInstance.Warning($"[game] IntegerItemUI.UpdateUI not found - fix for {what} not applied"); return; }
                HarmonyInstance.Patch(target, prefix: new HarmonyMethod(typeof(Mod), nameof(IntegerItemUpdateUI)));
                LoggerInstance.Msg($"[game {Application.version}] fixed: {what}");
            }
            catch (Exception e)
            {
                LoggerInstance.Warning($"[game] could not apply fix for {what}: {e.Message}");
            }
        }

        private static bool IntegerItemUpdateUI(Il2CppScheduleOne.UI.Items.IntegerItemUI __instance)
        {
#if S1UMF_DEV
            long __t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try {
#endif
            try
            {
                var value = __instance.integerItemInstance;
                var label = __instance.ValueLabel;
                if (value != null && label != null) return true; // the game's own path works
                if (__instance.Destroyed) return false;

                var item = __instance.itemInstance;
                if (_integerUiGuarded++ < 10)
                {
                    string id = "?", kind = "?";
                    try { id = item?.ID ?? "null item"; kind = item == null ? "-" : item.GetIl2CppType().FullName; } catch { }
                    MelonLogger.Warning($"[S1UMF] IntegerItemUI for '{id}' ({kind}) has " +
                                        (label == null ? "no value label" : "no integer value") + " - shown without it");
                }
                if (label != null) label.text = string.Empty;
                if (item != null)
                {
                    if (__instance.IconImg != null) __instance.IconImg.sprite = item.Icon;
                    __instance.SetDisplayedQuantity(item.Quantity);
                }
            }
            catch (Exception e)
            {
                if (_integerUiGuarded++ < 10) MelonLogger.Warning("[S1UMF] IntegerItemUI guard: " + e.Message);
            }
            return false;
#if S1UMF_DEV
            } finally { System.Threading.Interlocked.Increment(ref ProfCalls[0]); System.Threading.Interlocked.Add(ref ProfTicks[0], System.Diagnostics.Stopwatch.GetTimestamp() - __t0); }
#endif
        }

        // ---------------------------------------------------------------- K9 Patrol 1.1.0
        //
        // K9NPC.RequestPath only throttles once the agent HAS a path. While one is still being computed
        // (pathPending, hasPath false) every call issues SetDestination again, which restarts the
        // computation. When the navmesh is busy enough that a path takes more than one frame, it never
        // finishes: "K9 dog is STUCK ... hasPath=False pathPending=True". Skip the request while a path is
        // pending for (near enough) the same target, and throttle it like the mod does for a live path.

        // The dog's fields, read through accessors compiled once: RequestPath runs for every dog many times a
        // second, and FieldInfo.GetValue (with boxing) cost ~1.5 us a call.
        private static Type _k9Type;
        private static Func<object, NavMeshAgent> _agent;
        private static Func<object, Vector3> _lastDest;
        private static Func<object, float> _nextRepath;

        private static Func<object, T> Getter<T>(Type type, string field)
        {
            var info = AccessTools.Field(type, field);
            if (info == null) return null;
            var obj = System.Linq.Expressions.Expression.Parameter(typeof(object), "o");
            var read = System.Linq.Expressions.Expression.Field(System.Linq.Expressions.Expression.Convert(obj, type), info);
            return System.Linq.Expressions.Expression.Lambda<Func<object, T>>(
                System.Linq.Expressions.Expression.Convert(read, typeof(T)), obj).Compile();
        }

        private static bool K9RequestPath(object __instance, Vector3 destination)
        {
#if S1UMF_DEV
            long __t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try {
#endif
            try
            {
                var type = __instance.GetType();
                if (type != _k9Type)
                {
                    _agent = Getter<NavMeshAgent>(type, "_agent");
                    _lastDest = Getter<Vector3>(type, "_lastRequestedDestination");
                    _nextRepath = Getter<float>(type, "_nextRepathAt");
                    _k9Type = type;
                }
                if (_agent == null || _lastDest == null || _nextRepath == null) return true;

                var agent = _agent(__instance);
                if (agent == null || !agent.pathPending) return true;          // mod's own logic from here

                if ((destination - _lastDest(__instance)).sqrMagnitude < 0.5625f) return false;  // same target, let it finish
                return Time.time >= _nextRepath(__instance);                   // moved target: throttled
            }
            catch
            {
                return true;
            }
#if S1UMF_DEV
            } finally { System.Threading.Interlocked.Increment(ref ProfCalls[1]); System.Threading.Interlocked.Add(ref ProfTicks[1], System.Diagnostics.Stopwatch.GetTimestamp() - __t0); }
#endif
        }

        // ---------------------------------------------------------------- More Guns 1.6.6
        //
        // Its automatic fire loop fires every frame GameInput.GetButton(PrimaryClick) is true. 0.4.7 answers
        // that from a set of buttons currently down, updated by press and release events, and a click that
        // selects the weapon from the hotbar can leave PrimaryClick in that set. The gun then fires until it
        // is put away. Require the button to have been seen up, or pressed, since this weapon came out.

        private static IntPtr _weapon;
        private static bool _armed;

        private static bool MoreGunsAutoFire(Equippable_RangedWeapon __instance)
        {
            try
            {
                var ptr = __instance == null ? IntPtr.Zero : __instance.Pointer;
                if (ptr != _weapon)
                {
                    _weapon = ptr;
                    _armed = false;
                }
                if (!_armed)
                {
                    if (!GameInput.GetButton(GameInput.ButtonCode.PrimaryClick)
                        || GameInput.GetButtonDown(GameInput.ButtonCode.PrimaryClick))
                        _armed = true;
                    else
                        return false;                                            // click held from before
                }
                return true;
            }
            catch
            {
                return true;
            }
        }
    }
}
