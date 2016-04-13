using Microsoft.VisualStudio.TestTools.UnitTesting;
using DhtWalker2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Tancoder.Torrent.BEncoding;
using System.Security.Cryptography;

namespace DhtWalker2.Tests
{
    [TestClass()]
    public class SeedCargoTests
    {
        [TestMethod()]
        public void AddTest()
        {
            SeedCargo cargo = new SeedCargo();
            SHA1 sha1 = SHA1.Create();
            byte[] buffer = File.ReadAllBytes("test.torrent");
            var hash = sha1.ComputeHash(buffer);
            var info = BEncodedValue.Decode<BEncodedDictionary>(buffer);
            cargo.Add(info, hash);
            Assert.IsTrue(cargo.ExsistHash(hash));
        }

        [TestMethod()]
        public void GetSeedTest()
        {
            SeedCargo cargo = new SeedCargo(); 
            SHA1 sha1 = SHA1.Create();
            byte[] buffer = File.ReadAllBytes("test.torrent");
            var hash = sha1.ComputeHash(buffer);
            var info = BEncodedValue.Decode<BEncodedDictionary>(buffer);
            cargo.Add(info, hash);
            Assert.IsTrue(cargo.GetSeed(hash).Infohash.SequenceEqual(hash));
        }
    }
}