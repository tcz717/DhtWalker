using System;
using Tancoder.Torrent;

namespace DhtWalker2
{
    public class MongoSeedFilter : IMetaDataFilter
    {
        private SeedCargo cargo;

        public MongoSeedFilter(SeedCargo cargo)
        {
            this.cargo = cargo;
        }

        public SeedCargo Cargo
        {
            get
            {
                return cargo;
            }

            set
            {
                cargo = value;
            }
        }

        public bool Ignore(InfoHash metadata)
        {
            var result = cargo.ExsistHash(metadata.Hash);
            if (result)
                cargo.IncHot(metadata.Hash);
            return result;
        }
    }
}