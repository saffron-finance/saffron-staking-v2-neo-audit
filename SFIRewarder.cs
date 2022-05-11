using System;
using System.ComponentModel;
using System.Numerics;

using Neo;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

/**
 * @dev Implementation of the ISFIRewarder interface.
 *
 * This contract holds SFI tokens minted by governance for liquidity mining rewards.
 * It distributes rewards to users based on the SaffronStakingV2 contract. This is
 * an extension of the Sushiswap Masterchef contract. It allows SFI in the rewards
 * pool to be separate from SFI staked in single-asset staking via SaffronStakingV2.
 *
 * NOTE: Before opening staking send enough SFI here otherwise distribution can fail.
 *
 * NOTE: Partial rewards are impossible because the contract always tries to pay out 
 *       in full.
 */
 
namespace SFIContracts
{
    [DisplayName("Saffron.SFIRewarder")]
    [ManifestExtra("Author", "Saffron")]
    [ManifestExtra("Email", "dev@saffron.finance")]
    [ManifestExtra("Description", "SFIRewarder contract for holding SFI for the staking contract")]
    [ContractPermission("*", "transfer")]
    [SupportedStandards("NEP-17")] 
    //[ContractTrust("0x0a0b00ff00ff00ff00ff00ff00ff00ff00ff00a4")]

    public class SFIRewarder : SmartContract
    {

        [DisplayName("OwnershipTransferred")]
        public static event Action<UInt160, UInt160> onOwnershipTransferred;

        protected static Transaction tx => (Transaction) Runtime.ScriptContainer;

        /**
        * @dev Initializes the contract setting the deployer as the initial owner.
        */
        protected static void initOwner() {
            setOwner(tx.Sender);
        }

        /**
        * @dev Returns the address of the current owner.
        */
        public static UInt160 owner() {
            return (UInt160)Storage.Get(Storage.CurrentContext, "owner");
        }

        /**
        * @dev Throws if called by any account other than the owner.
        */
        public static void onlyOwner() {
            if (!Runtime.CheckWitness(owner())) throw new Exception("Ownable: caller is not the owner");
        }

        /**
        * @dev Leaves the contract without owner. It will not be possible to call
        * `onlyOwner` functions anymore. Can only be called by the current owner.
        *
        * NOTE: Renouncing ownership will leave the contract without an owner,
        * thereby removing any functionality that is only available to the owner.
        */
        public static void renounceOwnership() {
            onlyOwner();
            setOwner(UInt160.Zero);
        }

        /**
        * @dev Transfers ownership of the contract to a new account (`newOwner`).
        * Can only be called by the current owner.
        */
        public static void transferOwnership(UInt160 newOwner) {
            onlyOwner();
            if (!newOwner.IsValid) throw new Exception("Ownable: new owner is invalid");
            setOwner(newOwner);
        }

        private static void setOwner(UInt160 newOwner) {
            UInt160 oldOwner = owner();
            Storage.Put(Storage.CurrentContext, "owner", (ByteString) newOwner);
            onOwnershipTransferred(oldOwner, newOwner);
        }

    
    
        [DisplayName("UserRewarded")]
        public static event Action<UInt160, BigInteger> onUserRewarded;

        private static StorageMap contractStorage => new StorageMap(Storage.CurrentContext, "storage");

        // SFI governance token
        public static UInt160 sfiAddress => (UInt160) contractStorage.Get("sfiAddress");

        // SaffronStakingV2 contract address
        public static UInt160 saffronStaking => (UInt160) contractStorage.Get("saffronStaking");
    
        // contract constructor
        public static void _deploy(object data, bool update) {
            if (!update) initOwner();
        }

        public static void setSFIAddress(UInt160 sfiAddress) {
            onlyOwner();
            if (sfiAddress.IsZero || !sfiAddress.IsValid) throw new Exception("invalid SFI address");
            contractStorage.Put("sfiAddress", sfiAddress);
        }

        /**
        * @dev Set the address for the live SaffronStakingV2 contract.
        * @param _saffronStaking The address of the deployed SaffronStakingV2 contract.
        *
        * Note that the SFIRewarder  must be deployed before SaffronStakingV2 contract
        * and then this function called with the SaffronStakingV2 address
        */
        public static void setStakingAddress(UInt160 _saffronStaking) {
            onlyOwner();
            if (_saffronStaking.IsZero || !_saffronStaking.IsValid) throw new Exception("invalid staking address");
            contractStorage.Put("saffronStaking", _saffronStaking);
        }
 
        /**
        * @dev Reward the user with SFI. Should be called by the SaffronStakingV2 contract.
        * @param to The account to reward with SFI.
        * @param amount The amount of SFI to reward.
        */
        public static void rewardUser(UInt160 to, BigInteger amount) {
            onlyStaking();
            Contract.Call(sfiAddress, "transfer", CallFlags.All, new object[]{Runtime.ExecutingScriptHash, to, amount, null});
            onUserRewarded(to, amount);
        }

        /**
        * @dev Emergency withdraw all SFI from the contract.
        * @param token The NEP17 token address to withdraw from the contract.
        * @param to The account that will receive the withdrawn tokens.
        * @param amount The amount (wei) of tokens to be transferred.
        */
        public static void emergencyWithdraw(UInt160 token, UInt160 to, BigInteger amount) {
            onlyOwner();
            Contract.Call(token, "transfer", CallFlags.All, new object[]{Runtime.ExecutingScriptHash, to, amount, null});
        }

        /**
        * @dev Modifier to make a function callable only by a deployed SaffronStakingV2 contract.
        *
        * Requirements:
        *
        * - saffronStaking must be set by governance first.
        */
        private static void onlyStaking() {
            if (!Runtime.CheckWitness(saffronStaking)) throw new Exception("requires staking pool");
        }

        public static void onNEP17Payment(UInt160 from, BigInteger amount, object data ) {
            // get address of token being sent to this contract
            UInt160 asset = Runtime.CallingScriptHash;

            // can only add SFI tokens to the rewarder
            if(asset != sfiAddress) throw new Exception("not SFI token");
            //Mint(from, "OnNEP17Payment");
        }
    }
}
