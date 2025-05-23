namespace DynFurnSS2;

using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

class Program
{
    static void Main(string[] args)
    {
        string modPrefix = "DFSS2";
        string modFile = "SS2AOP_DynamicFurniture.esp";

        // SS2_PurchaseableFurniture_Template
        var outgoing = new Fallout4Mod(ModKey.FromFileName(modFile), Fallout4Release.Fallout4);

        List<string> masters = [
            "Fallout4.esm",
            "WorkshopFramework.esm",
            "SS2.esm",
            "DynamicFurniture.esp"
        ];

        var listings = new List<LoadOrderListing>();
        foreach (var master in masters) listings.Add(new(ModKey.FromFileName(master), enabled: true));
        var loadOrder = LoadOrder.Import<IFallout4ModGetter>(listings, GameRelease.Fallout4);

        var env = GameEnvironment.Typical.Builder<IFallout4Mod, IFallout4ModGetter>(GameRelease.Fallout4)
            .WithTargetDataFolder(args[0])
            .WithLoadOrder(loadOrder)
            .WithOutputMod(outgoing)
            .Build();

        ILinkCache linkCache = env.LoadOrder.ToImmutableLinkCache();

        outgoing.IsSmallMaster = true;
        outgoing.ModHeader.Author = "Samutz";

        var dfMod = Fallout4Mod.CreateFromBinaryOverlay(Path.Combine(args[0], "DynamicFurniture.esp"), Fallout4Release.Fallout4);

        linkCache.TryResolve<IMiscItemGetter>(FormKey.Factory("01F463:SS2.esm"), out var storeItemTemplate);
        if (storeItemTemplate is null) throw new ArgumentException("Couldn't get store item template");

        linkCache.TryResolve<IConstructibleObjectGetter>(FormKey.Factory("014050:SS2.esm"), out var cobjItemTemplate);
        if (cobjItemTemplate is null) throw new ArgumentException("Couldn't get COBJ item template");

        var furnFormList = outgoing.FormLists.AddNew();
        furnFormList.EditorID = $"{modPrefix}_FurnitureStoreItems";
        furnFormList.Items.Add(FormKey.Factory("01F447:SS2.esm")); // SS2_FLID_FurnitureStoreItems

        // process furniture
        foreach (var cobj in dfMod.ConstructibleObjects)
        {
            if (cobj.Categories is null || cobj.Categories.Any(c => c.FormKey.ToString() == "106D8F:Fallout4.esm")) continue; // has scrap keyword
            if (cobj.CreatedObject is null) continue;

            linkCache.TryResolve<IFurnitureGetter>(cobj.CreatedObject.FormKey, out var dynFurn); // original furniture
            if (dynFurn is null) continue;

            var shopItem = outgoing.MiscItems.DuplicateInAsNewRecord(storeItemTemplate); // misc item / store inventory item
            if (shopItem is null) continue;

            var cobjItem = outgoing.ConstructibleObjects.DuplicateInAsNewRecord(cobjItemTemplate); // cobj item / workshop menu item
            if (cobjItem is null) continue;

            var oldEdid = dynFurn.EditorID?[8..];
            var oldEdidParts = oldEdid?.Split("_");

            string clutter = "Clutter";
            if (oldEdidParts?.Last()=="Food") clutter = "Food";
            if (oldEdidParts?.Last()=="Drinks") clutter = "Drinks";
            if (oldEdidParts?.Last()=="DrinksFood" || oldEdidParts?.Last()=="FoodDrinks") clutter = "Food & Drinks";
            string shopName = $"[DF] {dynFurn?.Name?.ToString()} with {clutter}";
            if (oldEdidParts?[^2]=="SchoolDesk") shopName = $"[DF] {oldEdidParts.Last()} School Desk with {clutter}";
            if (oldEdidParts?[^2]=="Grill") shopName = $"[DF] {oldEdidParts.Last()} Grill with {clutter}";

            Furniture? furnItem = null;
            // store display versions to be used on store plot later
            if (dynFurn is not null)
            {
                furnItem = outgoing.Furniture.DuplicateInAsNewRecord(dynFurn);
                furnItem.EditorID = $"{modPrefix}_DemoFurn_{oldEdid}";
                furnItem.Name = shopName;

                FormKey WorkshopRelaxationObject = FormKey.Factory("05C2A3:Fallout4.esm");
                if (furnItem is not null && furnItem.HasKeyword(WorkshopRelaxationObject))
                    furnItem.Keywords.RemoveWhere(k => k.FormKey == WorkshopRelaxationObject);

                FormKey FurnitureClassRelaxation = FormKey.Factory("18F692:Fallout4.esm");
                if (furnItem is not null && furnItem.HasKeyword(FurnitureClassRelaxation))
                    furnItem.Keywords.RemoveWhere(k => k.FormKey == FurnitureClassRelaxation);

                FormKey WorkshopWorkObject = FormKey.Factory("020592:Fallout4.esm");
                linkCache.TryResolve<IKeywordGetter>(WorkshopWorkObject, out var WorkObjectKeyword);
                if (furnItem is not null && !furnItem.HasKeyword(WorkshopWorkObject) && WorkObjectKeyword is not null)
                    furnItem?.Keywords?.Add(WorkObjectKeyword);
                
                Console.WriteLine($"FurnitureClassWork: {WorkshopWorkObject}");
                Console.WriteLine($"ClassWorkKeyword: {WorkObjectKeyword}");

                FormKey FurnitureClassWork = FormKey.Factory("18F691:Fallout4.esm");
                linkCache.TryResolve<IKeywordGetter>(FurnitureClassWork, out var ClassWorkKeyword);
                if (furnItem is not null && !furnItem.HasKeyword(FurnitureClassWork) && ClassWorkKeyword is not null)
                    furnItem?.Keywords?.Add(ClassWorkKeyword);

                Console.WriteLine($"FurnitureClassWork: {FurnitureClassWork}");
                Console.WriteLine($"ClassWorkKeyword: {ClassWorkKeyword}");
            }
            
            // misc item record / store inventory item
            shopItem.EditorID = $"{modPrefix}_StoreItem_{oldEdid}";
            shopItem.Name = shopName;
            
            if (dynFurn?.Model is not null) shopItem.Model = dynFurn.Model.DeepCopy();

            int iVendorLevel = 1;
            if (shopItem?.Value is not null && oldEdidParts?[0]=="Stool") shopItem.Value = 25;
            if (shopItem?.Value is not null && oldEdidParts?[0]=="Chair") shopItem.Value = 30;
            if (shopItem?.Value is not null && oldEdidParts?[0]=="SchoolDesk") { shopItem.Value = 40;  iVendorLevel = 2; }
            if (shopItem?.Value is not null && oldEdidParts?[0]=="Grill") { shopItem.Value = 80;  iVendorLevel = 2; }
            if (shopItem?.Value is not null && oldEdidParts?[0]=="Table") { shopItem.Value = 125;  iVendorLevel = 3; }

            foreach (var script in shopItem?.VirtualMachineAdapter?.Scripts ?? [])
            {
                if (script.Name != "SimSettlementsV2:MiscObjects:FurnitureStoreItem") continue;

                script.Properties.Add(new ScriptIntProperty(){ Data = iVendorLevel, Name = "iVendorLevel" });
                script.Properties.Add(new ScriptIntProperty(){ Data = 0, Name = "iPositionGroup" });
            }

            if (shopItem is not null) furnFormList.Items.Add(shopItem.FormKey);

            // cobj record / workshop menu item
            cobjItem.EditorID = $"{modPrefix}_CO_{oldEdid}";

            cobjItem.CreatedObject.FormKey = cobj.CreatedObject.FormKey;
            if (cobjItem.Components?[0].Component.FormKey is not null && shopItem?.FormKey is not null)
            {
                cobjItem.Components[0].Component.FormKey = shopItem.FormKey;
            }
            int i = 0;
            foreach (var condition in cobjItem.Conditions)
            {
                if (
                    shopItem?.ToLink() is not null
                    && condition.Data is FunctionConditionData data
                    && data.Function == Condition.Function.GetItemCount &&
                    data.ParameterOneRecord is not null
                )
                {
                    data.ParameterOneRecord = shopItem.ToLink();
                }
                i++;
            }

            //Console.WriteLine($"Created records for: {oldEdid}");
        }

        Console.WriteLine($"Total furniture: {outgoing.ConstructibleObjects.Count()}");

        // SS2 addon global
        var modVersion = outgoing.Globals.AddNewFloat();
        modVersion.MajorFlags = Global.MajorFlag.Constant;
        modVersion.EditorID = $"{modPrefix}_ModVersion";
        modVersion.Data = 1.0f;
        
        // SS2 addon config
        linkCache.TryResolve<IMiscItemGetter>(FormKey.Factory("014B89:SS2.esm"), out var addonConfigTemplate);
        if (addonConfigTemplate is null) throw new ArgumentException("Couldn't get addon config template");

        var addonConfig = outgoing.MiscItems.DuplicateInAsNewRecord(addonConfigTemplate)
            ?? throw new ArgumentException("Couldn't create addon config record"); // cobj item / workshop menu item

        addonConfig.EditorID = $"{modPrefix}_AddonConfig";

        foreach (var script in addonConfig?.VirtualMachineAdapter?.Scripts ?? [])
        {
            if (script.Name!="SimSettlementsV2:MiscObjects:AddonPackConfiguration") continue;

            script.Properties.Add(new ScriptObjectProperty(){ Object = modVersion.ToLink(), Name = "MyVersionNumber" });
            script.Properties.Add(new ScriptStringProperty(){ Data = modFile, Name = "sAddonFilename" });

            var MyItemsProperty  = new ScriptObjectListProperty(){ Objects = [], Name = "MyItems" };
            MyItemsProperty.Objects.Add(new ScriptObjectProperty() { Object = furnFormList.ToLink() });
            script.Properties.Add(MyItemsProperty);
        }

        // SS2 addon quest
        linkCache.TryResolve<IQuestGetter>(FormKey.Factory("00EB1F:SS2.esm"), out var addonQuestTemplate);
        if (addonQuestTemplate is null) throw new ArgumentException("Couldn't get addon quest template");

        var addonQuest = outgoing.Quests.DuplicateInAsNewRecord(addonQuestTemplate)
            ?? throw new ArgumentException("Couldn't create addon quest record");

        addonQuest.EditorID = $"{modPrefix}_AddonQuest";

        foreach (var script in addonQuest?.VirtualMachineAdapter?.Scripts ?? [])
        {
            if (script.Name!="SimSettlementsV2:quests:AddonPack") continue;

            if (addonConfig is not null) script.Properties.Add(new ScriptObjectProperty(){ Object = addonConfig.ToLink(), Name = "MyAddonConfig" });
        }

        // write file
        outgoing.BeginWrite.ToPath(Path.Combine(args[0], outgoing.ModKey.FileName)).WithNoLoadOrder().Write();
    }
}