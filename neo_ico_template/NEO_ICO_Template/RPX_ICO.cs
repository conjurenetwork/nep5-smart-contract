using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.Numerics;

namespace RPX
{
    public class RPX : FunctionCode
    {

        public static Object Main(string operation, params object[] args)
        {
            string name = "RPX";
            string symbol = "RPX";
            BigInteger decimals = 8;
            if (!VerifyWithdrawal(operation)) return false;
            if(operation == "mintTokens") return MintTokens();
            if(operation == "totalSupply") return TotalSupply();
            if(operation == "name") return name;
            if(operation == "symbol") return symbol;
            if(operation == "transfer") return Transfer(args);
            if(operation == "balanceOf") return BalanceOf(args);
            if (operation == "deploy") return Deploy();
            if (operation == "refund") return Refund();
            if (operation == "withdrawal") return Withdrawal(args);
            if (operation == "decimals") return decimals;
            return false;
        }
        // initialization parameters, only once
        // 初始化参数
        private static bool Deploy()
        {
            byte[] owner = new byte[] { 2, 133, 234, 182, 95, 74, 1, 38, 228, 184, 91, 78, 93, 139, 126, 48, 58, 255, 126, 251, 54, 13, 89, 95, 46, 49, 137, 187, 144, 72, 122, 213, 170 };
            BigInteger pre_ico_cap = 30000000;
            uint decimals_rate = 100000000;
            byte[] total_supply = Storage.Get(Storage.CurrentContext, "totalSupply");
            if (total_supply.Length != 0)
            {
                return false;
            }
            Storage.Put(Storage.CurrentContext, owner, IntToBytes(pre_ico_cap * decimals_rate));
            Storage.Put(Storage.CurrentContext, "totalSupply", IntToBytes(pre_ico_cap * decimals_rate));
            return true;
        }
        // The function Withdrawal is only usable when contract owner want
        // to transfer neo from contract
        // 从智能合约提取neo币时，验证是否是智能合约所有者
        private static bool Withdrawal(object[] args)
        {
            if(args.Length != 1)
            {
                return false;
            }
            byte[] signature = (byte[])args[0];
            byte[] owner = Storage.Get(Storage.CurrentContext, "owner");
            return VerifySignature(owner, signature);
        }

        // The function MintTokens is only usable by the chosen wallet
        // contract to mint a number of tokens proportional to the
        // amount of neo sent to the wallet contract. The function
        // can only be called during the tokenswap period
        // 将众筹的neo转化为等价的RPX tokens
        private static bool MintTokens()
        {
            uint decimals_rate = 100000000;
            //检查转入资产是否为小蚁股
            byte[] neo_asset_id = new byte[] { 197, 111, 51, 252, 110, 207, 205, 12, 34, 92, 74, 179, 86, 254, 229, 147, 144, 175, 133, 96, 190, 14, 147, 15, 174, 190, 116, 166, 218, 255, 124, 155 };
            Transaction trans = (Transaction)ExecutionEngine.ScriptContainer;
            TransactionInput trans_input = trans.GetInputs()[0];
            byte[] prev_hash = trans_input.PrevHash;
            Transaction prev_trans = Blockchain.GetTransaction(prev_hash);
            TransactionOutput prev_trans_output = prev_trans.GetOutputs()[trans_input.PrevIndex];
            if (!BytesEqual(prev_trans_output.AssetId, neo_asset_id))
            {
                return false;
            }
            byte[] sender = prev_trans_output.ScriptHash;
            TransactionOutput[] trans_outputs = trans.GetOutputs();
            byte[] receiver = ExecutionEngine.ExecutingScriptHash;
            //统计转入到当前合约地址output的总和
            long value = 0;
            foreach (TransactionOutput trans_output in trans_outputs)
            {
                if (BytesEqual(trans_output.ScriptHash, receiver))
                {
                    value += trans_output.Value;

                }
            }
            //获得当前bonus并计算实际所得
            uint swap_rate = CurrentSwapRate();
            if (swap_rate == 0)
            {
                byte[] refund = Storage.Get(Storage.CurrentContext, "refund");
                byte[] sender_value = IntToBytes(value);
                byte[] new_refund = refund.Concat(sender.Concat(IntToBytes(sender_value.Length).Concat(sender_value)));
                Storage.Put(Storage.CurrentContext, "refund", new_refund);
                return false;
            }
            long token = value * swap_rate * decimals_rate;
            //落账
            BigInteger total_token = BytesToInt(Storage.Get(Storage.CurrentContext, sender));
            Storage.Put(Storage.CurrentContext, sender, IntToBytes(token + total_token));
            byte[] totalSypply = Storage.Get(Storage.CurrentContext, "totalSypply");
            Storage.Put(Storage.CurrentContext, "totalSypply", IntToBytes(token + BytesToInt(totalSypply)));
            return true;
        }
        // 获得ICO失败的退款列表
        private static byte[] Refund()
        {
            return Storage.Get(Storage.CurrentContext, "refund");
        }
        // 总发行量
        private static BigInteger TotalSupply()
        {
            byte[] totalSupply = Storage.Get(Storage.CurrentContext, "totalSypply");
            return BytesToInt(totalSupply);
        }

        private static bool Transfer(object[] args)
        {
            if (args.Length != 3) return false;
            byte[] from = (byte[])args[0];
            if (!Runtime.CheckWitness(from)) return false;
            byte[] to = (byte[])args[1];
            BigInteger value = BytesToInt((byte[])args[2]);
            if (value < 0) return false;
            byte[] from_value = Storage.Get(Storage.CurrentContext, from);
            byte[] to_value = Storage.Get(Storage.CurrentContext, to);
            BigInteger n_from_value = BytesToInt(from_value) - value;
            if (n_from_value < 0) return false;
            BigInteger n_to_value = BytesToInt(to_value) + value;
            Storage.Put(Storage.CurrentContext, from, IntToBytes(n_from_value));
            Storage.Put(Storage.CurrentContext, to, IntToBytes(n_to_value));
            Transferred(args);
            return true;
        }

        private static void Transferred(object[] args)
        {
            Runtime.Notify(args);
        }

        private static BigInteger BalanceOf(object[] args)
        {
            if (args.Length != 1) return 0;
            byte[] address = (byte[])args[0];
            byte[] balance = Storage.Get(Storage.CurrentContext, address);
            return BytesToInt(balance);
        }

        private static BigInteger BytesToInt(byte[] array)
        {
            var buffer = new BigInteger(array);
            return buffer;
        }

        private static byte[] IntToBytes(BigInteger value)
        {
            byte[] buffer = value.ToByteArray();
            return buffer;
        }

        private static bool BytesEqual(byte[] b1, byte[] b2)
        {
            if (b1.Length != b2.Length) return false;
            for (int i = 0; i < b1.Length; i++)
                if (b1[i] != b2[i])
                    return false;
            return true;
        }

        // transfer neo in smart contract can only invoke
        // the function Withdrawal
        // 当从智能合约中转出neo时，限定方法只能为WithDrawal方法
        private static bool VerifyWithdrawal(string operation)
        {
            if(operation == "withdrawal")
            {
                return true;
            }
            Transaction trans = (Transaction)ExecutionEngine.ScriptContainer;
            TransactionInput trans_input = trans.GetInputs()[0];
            Transaction prev_trans = Blockchain.GetTransaction(trans_input.PrevHash);
            TransactionOutput prev_trans_output = prev_trans.GetOutputs()[trans_input.PrevIndex];
            byte[] script_hash = ExecutionEngine.ExecutingScriptHash;
            if (BytesEqual(prev_trans_output.ScriptHash, script_hash))
            {
                return false;
            }
            return true;
        }
        // The function CurrentSwapRate() returns the current exchange rate
        // between rpx tokens and neo during the token swap period
        private static uint CurrentSwapRate()
        {
            BigInteger ico_start_time = 1505048400;
            BigInteger ico_end_time = 1506258000;
            uint exchange_rate = 1000;
            BigInteger total_amount = 1000000000;
            byte[] total_supply = Storage.Get(Storage.CurrentContext, "totalSupply");
            if(BytesToInt(total_supply) > total_amount)
            {
                return 0;
            }
            uint height = Blockchain.GetHeight();
            uint now = Blockchain.GetHeader(height).Timestamp;
            uint time = (uint)ico_start_time - now;
            if (time < 0)
            {
                return 0;
            }else if (time <= 86400)
            {
                return exchange_rate * 130 /100;
            }
            else if (time <= 259200)
            {
                return exchange_rate * 120 /100;
            }
            else if (time <= 604800)
            {
                return exchange_rate * 110 / 100;
            }
            else if (time <= 1209600)
            {
                return exchange_rate;
            }
            else
            {
                return 0;
            }

        }
    }
}
