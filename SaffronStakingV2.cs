using System;
using System.ComponentModel;
using System.Numerics;

using Neo;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;


/**
 * @dev Contract for rewarding users with SFI for the Saffron liquidity mining program.
 *
 * Code based off Sushiswap's Masterchef contract with the addition of SFIRewarder.
 * 
 * NOTE: Do not add pools with LP tokens that are deflationary or have reflection.
 */
 
namespace SFIContracts
{
    [DisplayName("Saffron.SaffronStakingV2")]
    [ManifestExtra("Author", "Saffron")]
    [ManifestExtra("Email", "dev@saffron.finance")]
    [ManifestExtra("Description", "SaffronStakingV2 contract for rewarding users with SFI for the Saffron liquidity mining program")]
    [ContractPermission("*","balanceOf","transfer","rewardUser")]
    [SupportedStandards("NEP-17")] 
    //[ContractTrust("0x0a0b00ff00ff00ff00ff00ff00ff00ff00ff00a4")]

    public class SaffronStakingV2 : SmartContract
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


        /**
        * @dev Emitted when `amount` tokens are deposited by `user` into pool id `pid`.
        */
        [DisplayName("TokensDeposited")]
        public static event Action<UInt160, BigInteger, BigInteger, BigInteger> onTokensDeposited;
 
        /**
        * @dev Emitted when `amount` tokens are withdrawn by `user` from pool id `pid`.
        */
        [DisplayName("TokensWithdrawn")]
        public static event Action<UInt160, BigInteger, BigInteger, BigInteger> onTokensWithdrawn;
 
        /**
        * @dev Emitted when `amount` tokens are emergency withdrawn by `user` from pool id `pid`.
        */
        [DisplayName("TokensEmergencyWithdrawn")]
        public static event Action<UInt160, BigInteger, BigInteger, BigInteger> onTokensEmergencyWithdrawn;
 
        /**
        * @dev Emitted when `sfiPerBlock` is set by governance.
        */
        [DisplayName("RewardPerBlockSet")]
        public static event Action<BigInteger> onRewardPerBlockSet;
 
        private static StorageMap contractStorage => new StorageMap(Storage.CurrentContext, "storage");
        private static StorageMap poolInfoStorage => new StorageMap(Storage.CurrentContext, "poolInfo");

        // Structure of user deposited amounts and their pending reward debt.
        public struct UserInfo {
            // Amount of tokens added by the user.
            public BigInteger amount;

            // Accounting mechanism. Prevents double-redeeming rewards in the same block.
            public BigInteger rewardDebt;

            public UserInfo(BigInteger _amount, BigInteger _rewardDebt) {
                amount = _amount;
                rewardDebt = _rewardDebt;
            }
        }

        // Structure holding information about each pool's LP token and allocation information.
        public struct PoolInfo {
            // LP token contract. In the case of single-asset staking this is an NEP17.
            public UInt160 lpToken;

            // Allocation points to determine how many SFI will be distributed per block to this pool.
            public BigInteger allocPoint;

            // The last block that accumulated rewards were calculated for this pool.
            public BigInteger lastRewardBlock; 

            // Accumulator storing the accumulated SFI earned per share of this pool.
            // Shares are user lpToken deposit amounts. This value is scaled up by 1e18.
            public BigInteger accSFIPerShare; 

            public PoolInfo(UInt160 _lpToken, BigInteger _allocPoint, BigInteger _lastRewardBlock, BigInteger _accSFIPerShare) {
                lpToken = _lpToken;
                allocPoint = _allocPoint;
                lastRewardBlock = _lastRewardBlock;
                accSFIPerShare = _accSFIPerShare;
            }
        }

        // The amount of SFI to be rewarded per block to all pools.
        public static BigInteger sfiPerBlock => (BigInteger) contractStorage.Get("sfiPerBlock");

        // SFI rewards are cut off after a specified block. Can be updated by governance to extend/reduce reward time.
        public static BigInteger rewardCutoff => (BigInteger) contractStorage.Get("rewardCutoff");

        // SFIRewarder contract holding the SFI tokens to be rewarded to users.
        public static UInt160 rewarder => (UInt160) contractStorage.Get("rewarder");

        /** 
        * @dev Return the number of pools in the poolInfo list.
        */
        public static BigInteger poolLength => (BigInteger) contractStorage.Get("poolLength");

        // Pool internal helper functions
        private static void pushPool(PoolInfo pool) {
            // save the current pool index for this lp token address
            lpTokenPIDMap.Put(pool.lpToken, poolLength);

            // flag that this address is added
            lpTokenAddedMap.Put(pool.lpToken, "True");

            // store pool at current poolLength index
            poolInfoStorage.PutObject(poolLength.ToString(), pool);

            // add 1 to the poolLength
            contractStorage.Put("poolLength",poolLength+1);
        }

        // List of pool info structs by pool id
        public static PoolInfo poolInfo(BigInteger index) {
            //check if pool exists
            if(index >= poolLength) throw new Exception("invalid pool");
            return (PoolInfo) poolInfoStorage.GetObject(index.ToString());
        }

        // Mapping to store list of added LP tokens to prevent accidentally adding duplicate pools.
        private static StorageMap lpTokenAddedMap => new StorageMap(Storage.CurrentContext, "lpTokenAdded");
        public static bool lpTokenAdded(UInt160 address) => lpTokenAddedMap.Get(address)=="True";

        // Mapping to store list of added LP tokens PID to look up for deposit
        private static StorageMap lpTokenPIDMap => new StorageMap(Storage.CurrentContext, "lpTokenPIDs");
        public static BigInteger lpTokenPID(UInt160 address) => (BigInteger) lpTokenPIDMap.Get(address);

        // Mapping of mapping to store user informaton indexed by pool id and the user's address.
        private static StorageMap userInfoStorage => new StorageMap(Storage.CurrentContext, "userInfo");

        //public static UserInfo userInfo(BigInteger pid, UInt160 address) => (UserInfo) userInfoStorage.GetObject(pid.ToString()+"_"+address.ToString());
        // https://discord.com/channels/382937847893590016/393072926556946433/860201549409419304
        public static UserInfo userInfo(BigInteger pid, UInt160 address) {
            var user = userInfoStorage.GetObject(pid.ToString()+"_"+(ByteString)address);
            if (user == null) {
                user = new UserInfo(BigInteger.Zero,BigInteger.Zero);
            }
            return (UserInfo)user;
        }
        // Total allocation points. Must be the sum of all allocation points in all pools.
        public static BigInteger totalAllocPoint => (BigInteger) contractStorage.Get("totalAllocPoint");
 
        // contract constructor
        public static void _deploy(object data, bool update) {
            if (!update) {
                initOwner();
                contractStorage.Put("poolLength", BigInteger.Zero);
                contractStorage.Put("totalAllocPoint", BigInteger.Zero);
            }
        }

        // initalize contract parameters
        public static void constructor(UInt160 _rewarder, BigInteger _sfiPerBlock, BigInteger _rewardCutoff) {
            onlyOwner();
            setRewarder(_rewarder);
            setRewardPerBlock(_sfiPerBlock);
            setRewardCutoff(_rewardCutoff);
        }

        /** 
        * @dev Update the SFIRewarder. Only callable by the contract owner.
        * @param _rewarder The new SFIRewarder account.
        */
        public static void setRewarder(UInt160 _rewarder) {
            onlyOwner();
            if (_rewarder.IsZero || !_rewarder.IsValid) throw new Exception("invalid rewarder");
            contractStorage.Put("rewarder", _rewarder);
        }

        /** 
        * @dev Update the amount of SFI rewarded per block. Only callable by the contract owner.
        * @param _sfiPerBlock The new SFI per block amount to be distributed.
        */
        public static void setRewardPerBlock(BigInteger _sfiPerBlock) {
            onlyOwner();
            massUpdatePools();
            contractStorage.Put("sfiPerBlock", _sfiPerBlock);
            onRewardPerBlockSet(_sfiPerBlock);
        }

        /** 
        * @dev Update the reward end block. Only callable by the contract owner.
        * @param _rewardCutoff The new cut-off block to end SFI reward distribution.
        */
        public static void setRewardCutoff(BigInteger _rewardCutoff) {
            onlyOwner();
            if (_rewardCutoff < Ledger.CurrentIndex) throw  new Exception("invalid rewardCutoff");
            contractStorage.Put("rewardCutoff", _rewardCutoff);
        }

        /** 
        * @dev Update the reward end block and sfiPerBlock atomically. Only callable by the contract owner.
        * @param _rewardCutoff The new cut-off block to end SFI reward distribution.
        * @param _sfiPerBlock The new SFI per block amount to be distributed.
        */
        public static void setRewardPerBlockAndRewardCutoff(BigInteger _sfiPerBlock, BigInteger _rewardCutoff) {
            onlyOwner();
            massUpdatePools();
            setRewardPerBlock(_sfiPerBlock);
            setRewardCutoff(_rewardCutoff);
        }

        /**
        * @dev Add a new pool specifying its lp token and allocation points.
        * @param _allocPoint The allocationPoints for the pool. Determines SFI per block.
        * @param _lpToken Token address for the LP token in this pool.
        */
        public static void add(BigInteger _allocPoint, UInt160 _lpToken) {
            onlyOwner();
            if (_lpToken.IsZero || !_lpToken.IsValid) throw new Exception("invalid _lpToken address");
            if (lpTokenAdded(_lpToken)) throw new Exception("lpToken already added");
            if (Ledger.CurrentIndex >= rewardCutoff) throw new Exception("can't add pool after cutoff");
            if (_allocPoint <= 0) throw new Exception("can't add pool with 0 ap");
            massUpdatePools();
            contractStorage.Put("totalAllocPoint", totalAllocPoint + _allocPoint);
            
            PoolInfo pool = new PoolInfo(_lpToken, _allocPoint, Ledger.CurrentIndex, BigInteger.Zero);
            pushPool(pool);

        }

        /**
        * @dev Set the allocPoint of the specific pool with id _pid.
        * @param _pid The pool id that is to be set.
        * @param _allocPoint The new allocPoint for the pool.
        */
        public static void set(BigInteger _pid, BigInteger _allocPoint) {
            onlyOwner();
            if (_pid >= poolLength) throw new Exception("can't set non-existent pool");
            if (_allocPoint <= 0) throw new Exception("can't set pool with 0 ap");
            massUpdatePools();
            PoolInfo pool = poolInfo(_pid);
            // update totalAllocPoint
            contractStorage.Put("totalAllocPoint", totalAllocPoint - pool.allocPoint + _allocPoint);
            // update pool _allocPoint and store pool at current _pid index
            pool.allocPoint = _allocPoint;
            poolInfoStorage.PutObject(_pid.ToString(), pool);
        }

        /**
        * @dev Return the pending SFI rewards of a user for a specific pool id.
        *
        * Helper function for front-end web3 implementations.
        *
        * @param _pid Pool id to get SFI rewards report from.
        * @param _user User account to report SFI rewards from.
        * @return Pending SFI amount for the user indexed by pool id.
        */
        public static BigInteger pendingSFI(BigInteger _pid, UInt160 _user) {
            if (_pid >= poolLength) throw new Exception("non-existent pool");
            PoolInfo pool = poolInfo(_pid);
            UserInfo user = userInfo(_pid, _user);
            BigInteger accSFIPerShare = pool.accSFIPerShare;
            BigInteger lpSupply = (BigInteger) Contract.Call(pool.lpToken, "balanceOf", CallFlags.ReadOnly, new object[]{Runtime.ExecutingScriptHash});
            
            BigInteger latestRewardBlock = Ledger.CurrentIndex >= rewardCutoff ? rewardCutoff : Ledger.CurrentIndex;

            if (latestRewardBlock > pool.lastRewardBlock && lpSupply != 0) {
                // Get number of blocks to multiply by
                BigInteger multiplier = latestRewardBlock - pool.lastRewardBlock;
                // New SFI reward is the number of blocks multiplied by the SFI per block times multiplied by the pools share of the total
                BigInteger sfiReward = multiplier * sfiPerBlock * pool.allocPoint;
                // Add delta/change in share of the new reward to the accumulated SFI per share for this pool's token
                accSFIPerShare = accSFIPerShare + (sfiReward * BigInteger.Pow(10,18) / lpSupply / totalAllocPoint);
            }
            // Return the pending SFI amount for this user
            return (user.amount * accSFIPerShare / BigInteger.Pow(10,18)) - user.rewardDebt;
        }

        /**
        * @dev Update reward variables for all pools. Be careful of gas spending! More than 100 pools is not recommended.
        */
        public static void massUpdatePools() {
            for (BigInteger pid = 0; pid < poolLength; ++pid) {
                updatePool(pid);
            }
        }

        /**
        * @dev Update accumulated SFI shares of the specified pool.
        * @param _pid The id of the pool to be updated.
        */
        public static PoolInfo updatePool(BigInteger _pid) {
            if (_pid >= poolLength) throw new Exception("non-existent pool");
            PoolInfo pool = poolInfo(_pid);

            // Only reward SFI for blocks earlier than rewardCutoff block
            BigInteger latestRewardBlock = Ledger.CurrentIndex >= rewardCutoff ? rewardCutoff : Ledger.CurrentIndex;

            // Don't update twice in the same block
            if (latestRewardBlock > pool.lastRewardBlock) {
                // Get the amount of this pools token owned by the SaffronStaking contract
                BigInteger lpSupply = (BigInteger) Contract.Call(pool.lpToken, "balanceOf", CallFlags.ReadOnly, new object[]{Runtime.ExecutingScriptHash});
                // Calculate new rewards if amount is greater than 0
                if (lpSupply > 0) {
                    // Get number of blocks to multiply by
                    BigInteger multiplier = latestRewardBlock - pool.lastRewardBlock;
                    // New SFI reward is the number of blocks multiplied by the SFI per block times multiplied by the pools share of the total
                    BigInteger sfiReward = multiplier * sfiPerBlock * pool.allocPoint;
                    // Add delta/change in share of the new reward to the accumulated SFI per share for this pool's token
                    pool.accSFIPerShare = pool.accSFIPerShare + (sfiReward * BigInteger.Pow(10,18) / lpSupply / totalAllocPoint);
                } 
                // Set the last reward block to the most recent reward block
                pool.lastRewardBlock = latestRewardBlock;
                poolInfoStorage.PutObject(_pid.ToString(), pool);
            }

            // Return this pools updated info
            return pool;
        }

        /**
        * @dev Deposit the user's lp token into the the specified pool.
        * @param _pid Pool id where the user's asset is being deposited.
        * @param _amount Amount to deposit into the pool.
        */
        public static void deposit(BigInteger _pid, BigInteger _amount) {
            // Get pool identified by pid
            if (_pid >= poolLength) throw new Exception("non-existent pool");
            PoolInfo pool = updatePool(_pid);
            // Get user in this pool identified by msg.sender address
            UserInfo user = userInfo(_pid, tx.Sender);
            // Calculate pending SFI earnings for this user in this pool
            BigInteger pending = (user.amount * pool.accSFIPerShare / BigInteger.Pow(10,18)) - user.rewardDebt;

            // Effects
            // Add the new deposit amount to the pool user's amount total
            user.amount = user.amount + _amount;
            // Update the pool user's reward debt to this new amount
            user.rewardDebt = user.amount * pool.accSFIPerShare / BigInteger.Pow(10,18);
            userInfoStorage.PutObject(_pid.ToString()+"_"+(ByteString)tx.Sender, user);

            // Interactions
            // Transfer pending SFI rewards to the user
            safeSFITransfer(tx.Sender, pending);
            
            // Transfer the users tokens to this contract (deposit them in this contract)
            // This is already done, when the user send tokens to this contract, the pool was looked up and then deposit was called
            // Contract.Call(pool.lpToken, "transfer", CallFlags.All, new object[]{tx.Sender, Runtime.ExecutingScriptHash, _amount, null});

            onTokensDeposited(tx.Sender, _pid, _amount, (BigInteger) Contract.Call(pool.lpToken, "balanceOf", CallFlags.ReadOnly, new object[]{Runtime.ExecutingScriptHash}));
        }

        /**
        * @dev Withdraw the user's lp token from the specified pool.
        * @param _pid Pool id from which the user's asset is being withdrawn.
        * @param _amount Amount to withdraw from the pool.
        */
        public static void withdraw(BigInteger _pid, BigInteger _amount) {
            // Get pool identified by pid
            if (_pid >= poolLength) throw new Exception("non-existent pool");
            PoolInfo pool = updatePool(_pid);
            // Get user in this pool identified by msg.sender address
            UserInfo user = userInfo(_pid, tx.Sender);
            if (user.amount < _amount) throw new Exception("can't withdraw more than user balance");
            // Calculate pending SFI earnings for this user in this pool
            BigInteger pending = (user.amount * pool.accSFIPerShare / BigInteger.Pow(10,18)) - user.rewardDebt;

            // Effects
            // Subtract the new withdraw amount from the pool user's amount total
            user.amount = user.amount - _amount;
            // Update the pool user's reward debt to this new amount
            user.rewardDebt = user.amount * pool.accSFIPerShare / BigInteger.Pow(10,18);
            userInfoStorage.PutObject(_pid.ToString()+"_"+(ByteString)tx.Sender, user);

            // Interactions
            // Transfer pending SFI rewards to the user
            safeSFITransfer(tx.Sender, pending);
            // Transfer contract's tokens amount to this user (withdraw them from this contract)
            Contract.Call(pool.lpToken, "transfer", CallFlags.All, new object[]{Runtime.ExecutingScriptHash, tx.Sender, _amount, null});

            onTokensWithdrawn(tx.Sender, _pid, _amount, (BigInteger) Contract.Call(pool.lpToken, "balanceOf", CallFlags.ReadOnly, new object[]{Runtime.ExecutingScriptHash}));
        }

        /**
        * @dev Emergency function to withdraw a user's asset in a specified pool.
        * @param _pid Pool id from which the user's asset is being withdrawn.
        */
        public static void emergencyWithdraw(BigInteger _pid) {
            if (_pid >= poolLength) throw new Exception("non-existent pool");
            PoolInfo pool = updatePool(_pid);
            UserInfo user = userInfo(_pid, tx.Sender);
            BigInteger amount = user.amount;

            // Effects
            user.amount = 0;
            user.rewardDebt = 0;
            userInfoStorage.PutObject(_pid.ToString()+"_"+(ByteString)tx.Sender, user);

            // Interactions
            Contract.Call(pool.lpToken, "transfer", CallFlags.All, new object[]{Runtime.ExecutingScriptHash, tx.Sender, amount, null});

            onTokensEmergencyWithdrawn(tx.Sender, _pid, amount, (BigInteger) Contract.Call(pool.lpToken, "balanceOf", CallFlags.ReadOnly, new object[]{Runtime.ExecutingScriptHash}));
        }

        /**
        * @dev Transfer SFI from the SFIRewarder contract to the user's account.
        * @param to Account to transfer SFI to from the SFIRewarder contract.
        * @param amount Amount of SFI to transfer from the SFIRewarder to the user's account.
        */
        public static void safeSFITransfer(UInt160 to, BigInteger amount) {
            if (amount > 0) {
                Contract.Call(rewarder, "rewardUser", CallFlags.All, new object[]{to, amount});
            }
        }

        public static void onNEP17Payment(UInt160 from, BigInteger amount, object data ) {
            UInt160 asset = Runtime.CallingScriptHash;

            // check if there is a pool for this lp token
            if (lpTokenAdded(asset)) {
                // find pool id for this address
                BigInteger pid = lpTokenPID(asset);

                // perform deposit tasks of amount to appropriate pool 
                deposit(pid, amount);

            } else {
                throw new Exception("lpToken not found");
            }
            //Mint(from, "OnNEP17Payment");
        }
    }
}
