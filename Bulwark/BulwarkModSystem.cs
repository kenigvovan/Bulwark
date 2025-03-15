using Bulwark.src;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;


namespace Bulwark {
    public class BulwarkModSystem : ModSystem {

        public static Config config;

        public override bool ShouldLoad(EnumAppSide forSide) => true;
        public override void Start(ICoreAPI api) {

            base.Start(api);
            api.RegisterBlockClass("BlockBarricade", typeof(BlockBarricade));
            api.RegisterBlockBehaviorClass("Flag", typeof(BlockBehaviorFlag));
            api.RegisterBlockBehaviorClass("Logistic", typeof(BlockBehaviorLogistic));
            api.RegisterBlockEntityBehaviorClass("FlagEntity", typeof(BlockEntityBehaviorFlag));
            api.RegisterBlockEntityBehaviorClass("LogisticEntity", typeof(BlockEntityBehaviorLogistic));

            config = api.LoadModConfig<Config>("BulwarkModConfig.json");
            if(config == null)
            {
                config = new Config();
            }
             api.StoreModConfig<Config>(config, "BulwarkModConfig.json");
        } // void ..
        

        public override void AssetsFinalize(ICoreAPI api) {
            base.AssetsFinalize(api);
            foreach (Block block in api.World.Blocks) {
                if (config.AllStoneBlockRequirePickaxe
                    && block.BlockMaterial      == EnumBlockMaterial.Stone
                    && block.Replaceable        <= 200
                    && block.CollisionBoxes     != null
                    && block.RequiredMiningTier <  2
                ) block.RequiredMiningTier = 2;
                
                if (block is BlockDoor || block.HasBehavior<BlockBehaviorDoor>()) {
                    if (block.BlockMaterial == EnumBlockMaterial.Metal && block.RequiredMiningTier < 3)
                        block.RequiredMiningTier = 3;
                    else if (block.BlockMaterial == EnumBlockMaterial.Wood && block.RequiredMiningTier < 2)
                        block.RequiredMiningTier = block.Code.EndVariant() == "crude" ? 1 : 2;
                } // if ..
            } // foreach ..
        } // void ..
    } // class ..
} // namespace ..
