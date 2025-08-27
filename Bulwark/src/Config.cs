using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bulwark.src
{
    public class Config
    {
        public float ClaimDurationPerSatiety = 0.0025f;
        public int UndergroundClaimLimit = 8;
        public bool AllStoneBlockRequirePickaxe = true;
        public int SecondsBeforeStrongholdOffline = 300;
        public int FortProtectionRadius = 32;
        public int CityProtectionRadius = 48;
        public int SecondCheckForOfflineStronghold = 10;
        public Dictionary<string, int> BlockBrakeTiers = new Dictionary<string, int>
        {
            { "game:bonysoil", 1 },
            { "game:claybricks-*", 2 },
            { "game:planks-*", 1 }
        };
    }
}
