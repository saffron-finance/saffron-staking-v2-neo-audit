using System;
using System.ComponentModel;
using System.Numerics;

using Neo;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

/*
- SFI token
NgBXuFnVvg7ijdYHPpYDsQxdzEZaW6rsFU
0xde54108c16c741d72aca511642b61f738433cf68 
*/

namespace SFIContracts
{
    [DisplayName("Saffron.Spice")]
    [ManifestExtra("Author", "Saffron")]
    [ManifestExtra("Email", "dev1@saffron.finance")]
    [ManifestExtra("Description", "SFI NEP17 token contract")]
    [SupportedStandards("NEP-17")]
    [ContractPermission("*", "onNEP17Payment")]
    public class SFIToken : SmartContract
    {

        // initial supply of SFI
        static readonly BigInteger initialSupply = BigInteger.Parse("100000000000000000000000"); // 100,000 SFI

        // storage for totalSupply, owner, balances
        private static StorageMap contractStorage => new StorageMap(Storage.CurrentContext, "storage");
        private static StorageMap addressBalances => new StorageMap(Storage.CurrentContext, "balances");
 
        // NEP 17 standard functions
        public static BigInteger totalSupply() => (BigInteger) contractStorage.Get("totalSupply");
        public static string symbol() => "SFI";
        public static ulong decimals() => 18;
        
        public static BigInteger balanceOf(UInt160 address) {
            return getBalance(address);
        } 
        
        // NEP 17 extra functions
        public static string name() => "Spice";
        public static UInt160 getOwner() {
            return (UInt160)contractStorage.Get("owner");
        }

        // internal helper functions
        private static BigInteger getBalance(UInt160 address) => (BigInteger) addressBalances.Get(address);
        private static void putBalance(UInt160 address, BigInteger amount) => addressBalances.Put(address, amount);

        private static Transaction tx => (Transaction) Runtime.ScriptContainer;

        private static Boolean isOwner() => Runtime.CheckWitness(getOwner());
            // return owner.Equals(Runtime.CallingScriptHash) || Runtime.CheckWitness(owner);

        private static void mustBeOwner() {
            if (!isOwner()) throw new Exception("must be owner");
        }
        
        private static void add(UInt160 address, BigInteger amount) {
            // add this amount to balance of address
            putBalance(address, getBalance(address) + amount);
        }

        private static void subtract(UInt160 address, BigInteger amount) {
            // get previous amount
            var amountPrev = getBalance(address);
            // check if previous amount is the same as subtracted amount
            if (amountPrev == amount) {
                // delete this address as balance is 0
                addressBalances.Delete(address);
            } else if (amount > amountPrev) {
                // if amount is more than available throw an error
                throw new Exception("subtraction underflow");
            } else {
                // subtract this amount from balance of address
                putBalance(address, amountPrev - amount);
            }
        }
        
        // NEP 17 event
        [DisplayName("Transfer")]
        public static event Action<UInt160, UInt160, BigInteger> onTransfer;

        // contract constructor
        public static void _deploy(object data, bool update) {
            if (!update) {
                contractStorage.Put("owner", (ByteString) tx.Sender);

                var owner = (Neo.UInt160) tx.Sender;
                add(owner, initialSupply);
                onTransfer(null, owner, initialSupply); 
                contractStorage.Put("totalSupply", initialSupply); 
            }
         }

        // contract updater
        public static void update(ByteString nefFile, String manifest, object data) {
            mustBeOwner();
            ContractManagement.Update(nefFile, manifest, data);
        }

        // contract verify
        public static bool verify() => isOwner();

        // contract destroy
        public static void destroy() {
            mustBeOwner();
            ContractManagement.Destroy();
        }

        public static bool mint(UInt160 address, BigInteger amount) {
            // // check to address is valid
            // if (!address.IsValid) throw new Exception("invalid address");

            // // check amount is positive integer
            // if (amount <= 0) throw new Exception("amount must be greater than zero");

            // // must be owner to mint
            // mustBeOwner();

            // // increase the balance of "to" and totalSupply by amount
            // add(address, amount);
            // contractStorage.Put("totalSupply", totalSupply() + amount); 

            // // emit Transfer event
            // onTransfer(Neo.UInt160.Zero, address, amount);

            // // The NEP-17 standard also requires that we check whether the recipient address is a contract; 
            // // if so, we must invoke the onPayment method of that contract 
            // // (this gives the recipient contract the opportunity to abort the transaction if it does not want to receive the XYZ tokens).
            // if (ContractManagement.GetContract(address) != null) {
            //     Contract.Call(address, "onNEP17Payment", CallFlags.All, new object[] { Neo.UInt160.Zero, amount, null });
            // }
            
            // return true;

            return false;
        }   

        public static bool transfer(UInt160 from, UInt160 to, BigInteger amount, object data) {
            // check from address is valid
            if (!from.IsValid) throw new Exception("invalid from address");

            // check to address is valid
            if (!to.IsValid) throw new Exception("invalid to address");

            // check amount is positive integer
            if (amount <= 0) throw new Exception("amount must be greater than zero");

            // check sender is authorized
            if (!from.Equals(Runtime.CallingScriptHash) && !Runtime.CheckWitness(from)) throw new Exception("not authorized");
             
            // check balance
            if (getBalance(from) < amount) throw new Exception("insufficent balance");

            // subtract amount from the 'from' address and add it to the 'to' address, then emit a Transfer event
            subtract(from, amount);
            add(to, amount);
            onTransfer(from, to, amount);

            // The NEP-17 standard also requires that we check whether the recipient address is a contract; 
            // if so, we must invoke the onPayment method of that contract 
            // (this gives the recipient contract the opportunity to abort the transaction if it does not want to receive the XYZ tokens).
            // https://dojo.coz.io/article/boa_nep_17
            if (ContractManagement.GetContract(to) != null) {
                Contract.Call(to, "onNEP17Payment", CallFlags.All, new object[] { from, amount, data });
            }
            
            return true;
        }

        public static void transferOwnership(UInt160 newOwner) {
            // check to newOwner is valid
            if (!newOwner.IsValid) throw new Exception("invalid address");

            // must be owner to transfer ownership
            mustBeOwner();

            // update owner
            contractStorage.Put("owner", (ByteString) newOwner);
        }

    }
}
