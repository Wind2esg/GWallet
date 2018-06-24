using NBitcoin;
using Newtonsoft.Json.Linq;
using QBitNinja.Client;
using QBitNinja.Client.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using HBitcoin.KeyManagement;
using static System.Console;

namespace GWallet
{
    public class Program
    {
        #region Commands
        //public static HashSet<string> Commands = new HashSet<string>(){
        //    "help",
        //    "generate-wallet",
        //    "recover-wallet",
        //    "show-balances",
        //    "show-history",
        //    "receive",
        //    "send"
        //};
        #endregion

        
        public void GenerateWallet(string path = "")
        { 
            string walletFilePath = GetWalletFilePath(path);
            AssertWalletNotExists(walletFilePath);

            string pw;
            string pwConf;
            while(true)
            {
                WriteLine("Choose a password:");
                pw = PasswordConsole.ReadPassword();
                WriteLine("Confirm password:");
                pwConf = PasswordConsole.ReadPassword();
                if (pw == pwConf)
                {
                    break;
                }
                WriteLine("Passwords do not match. Try again!");
            }


            Mnemonic mnemonic;
            Safe safe = Safe.Create(out mnemonic, pw, walletFilePath, Config.network);
            // 若无异常，则成功创建钱包
            WriteLine();
            WriteLine("Wallet is successfully created.");
            WriteLine($"Wallet file: {walletFilePath}");

            WriteLine();
            WriteLine("Write down the following mnemonic words.");
            WriteLine("With the mnemonic words AND your password you can recover this wallet by using the recover-wallet command.");
            WriteLine();
            WriteLine("-------");
            WriteLine(mnemonic);
            WriteLine("-------");
            File.WriteAllText(walletFilePath.Split('.')[0] + "_mnemonic.txt", mnemonic.ToString());
            WriteLine("mnemonic is saved in file " + walletFilePath.Split('.')[0] + "_mnemonic.txt");
        }

        public void RecoverWallet(string path = "")
        {
            var walletFilePath = GetWalletFilePath(path);
            AssertWalletNotExists(walletFilePath);

            WriteLine($"Your software is configured using the Bitcoin {Config.network} network.");
            WriteLine("Provide your mnemonic words, separated by spaces:");
            string mnemonicString = ReadLine();
            AssertCorrectMnemonicFormat(mnemonicString);
            Mnemonic mnemonic = new Mnemonic(mnemonicString);

            WriteLine("Provide your password. Please note the wallet cannot check if your password is correct or not. If you provide a wrong password a wallet will be recovered with your provided mnemonic AND password pair:");
            string password = PasswordConsole.ReadPassword();

            Safe safe = Safe.Recover(mnemonic, password, walletFilePath, Config.network);
            // 若无异常抛出，则成功恢复
            WriteLine();
            WriteLine("Wallet is successfully recovered.");
            WriteLine($"Wallet file: {walletFilePath}");
        }

        public void ShowBalances(string path = "")
        {
            var walletFilePath = GetWalletFilePath(path);
            Safe safe = DecryptWalletByAskingForPassword(walletFilePath);
            if (Config.connectionType == ConnectionType.Http)
            {
                // 查询所有选项，以地址分组
                Dictionary<BitcoinAddress, List<BalanceOperation>> operationsPerAddresses = QBitNinjaJutsus.QueryOperationsPerSafeAddresses(safe, 7);

                // 读取地址历史记录
                var addressHistoryRecords = new List<AddressHistoryRecord>();
                foreach (var elem in operationsPerAddresses)
                {
                    foreach (var op in elem.Value)
                    {
                        addressHistoryRecords.Add(new AddressHistoryRecord(elem.Key, op));
                    }
                }

                // 计算钱包余额
                Money confirmedWalletBalance;
                Money unconfirmedWalletBalance;
                QBitNinjaJutsus.GetBalances(addressHistoryRecords, out confirmedWalletBalance, out unconfirmedWalletBalance);

                // 再分组
                var addressHistoryRecordsPerAddresses = new Dictionary<BitcoinAddress, HashSet<AddressHistoryRecord>>();
                foreach (var address in operationsPerAddresses.Keys)
                {
                    var recs = new HashSet<AddressHistoryRecord>();
                    foreach (var record in addressHistoryRecords)
                    {
                        if (record.Address == address)
                            recs.Add(record);
                    }
                    addressHistoryRecordsPerAddresses.Add(address, recs);
                }

                // 计算地址余额
                WriteLine();
                WriteLine("---------------------------------------------------------------------------");
                WriteLine("Address\t\t\t\t\tConfirmed\tUnconfirmed");
                WriteLine("---------------------------------------------------------------------------");
                foreach (var elem in addressHistoryRecordsPerAddresses)
                {
                    Money confirmedBalance;
                    Money unconfirmedBalance;
                    QBitNinjaJutsus.GetBalances(elem.Value, out confirmedBalance, out unconfirmedBalance);
                    if (confirmedBalance != Money.Zero || unconfirmedBalance != Money.Zero)
                        WriteLine($"{elem.Key.ToString()}\t{confirmedBalance.ToDecimal(MoneyUnit.BTC).ToString("0.#############################")}\t\t{unconfirmedBalance.ToDecimal(MoneyUnit.BTC).ToString("0.#############################")}");
                }
                WriteLine("---------------------------------------------------------------------------");
                WriteLine($"Confirmed Wallet Balance: {confirmedWalletBalance.ToDecimal(MoneyUnit.BTC).ToString("0.#############################")}btc");
                WriteLine($"Unconfirmed Wallet Balance: {unconfirmedWalletBalance.ToDecimal(MoneyUnit.BTC).ToString("0.#############################")}btc");
                WriteLine("---------------------------------------------------------------------------");
            }
            else if (Config.connectionType == ConnectionType.FullNode)
            {
                throw new NotImplementedException();
            }
            else
            {
                Exit("Invalid connection type.");
            }
        }

        public void ShowHistory(string path = "")
        {
            var walletFilePath = GetWalletFilePath(path);
            Safe safe = DecryptWalletByAskingForPassword(walletFilePath);
            if (Config.connectionType == ConnectionType.Http)
            {
                // 查询所有选项，以地址分组
                Dictionary<BitcoinAddress, List<BalanceOperation>> operationsPerAddresses = QBitNinjaJutsus.QueryOperationsPerSafeAddresses(safe);

                WriteLine();
                WriteLine("---------------------------------------------------------------------------");
                WriteLine("Date\t\t\tAmount\t\tConfirmed\tTransaction Id");
                WriteLine("---------------------------------------------------------------------------");

                Dictionary<uint256, List<BalanceOperation>> operationsPerTransactions = QBitNinjaJutsus.GetOperationsPerTransactions(operationsPerAddresses);

                // 从交易历史记录中创建历史记录
                var txHistoryRecords = new List<Tuple<DateTimeOffset, Money, int, uint256>>();
                foreach (var elem in operationsPerTransactions)
                {
                    var amount = Money.Zero;
                    foreach (var op in elem.Value)
                        amount += op.Amount;
                    var firstOp = elem.Value.First();

                    txHistoryRecords.Add(new Tuple<DateTimeOffset, Money, int, uint256>(
                            firstOp.FirstSeen,
                            amount,
                            firstOp.Confirmations,
                            elem.Key));
                }

                // 排序 ( QBitNinja 存在 bug, 需要使用 DateTime)
                var orderedTxHistoryRecords = txHistoryRecords
                    .OrderByDescending(x => x.Item3) // Confirmations
                    .ThenBy(x => x.Item1); // FirstSeen
                foreach (var record in orderedTxHistoryRecords)
                {
                    // Item2 is the Amount
                    if (record.Item2 > 0) ForegroundColor = ConsoleColor.Green;
                    else if (record.Item2 < 0) ForegroundColor = ConsoleColor.Red;
                    WriteLine($"{record.Item1.DateTime}\t{record.Item2}\t{record.Item3 > 0}\t\t{record.Item4}");
                    ResetColor();
                }
            }
            else if (Config.connectionType == ConnectionType.FullNode)
            {
                throw new NotImplementedException();
            }
            else
            {
                Exit("Invalid connection type.");
            }
        }

        public void Receive(string path = "")
        {
            var walletFilePath = GetWalletFilePath(path);
            Safe safe = DecryptWalletByAskingForPassword(walletFilePath);

            if (Config.connectionType == ConnectionType.Http)
            {
                Dictionary<BitcoinAddress, List<BalanceOperation>> operationsPerReceiveAddresses = QBitNinjaJutsus.QueryOperationsPerSafeAddresses(safe, 7, HdPathType.Receive);

                WriteLine("---------------------------------------------------------------------------");
                WriteLine("Unused Receive Addresses");
                WriteLine("---------------------------------------------------------------------------");
                foreach (var elem in operationsPerReceiveAddresses)
                    if (elem.Value.Count == 0)
                        WriteLine($"{elem.Key.ToString()}");
            }
            else if (Config.connectionType == ConnectionType.FullNode)
            {
                throw new NotImplementedException();
            }
            else
            {
                Exit("Invalid connection type.");
            }
        }

        public void Send(string address, string btc = "all", string path = "")
        {
            var walletFilePath = GetWalletFilePath(path);
            BitcoinAddress addressToSend;
            try
            {
                addressToSend = BitcoinAddress.Create(address, Config.network);
            }
            catch (Exception ex)
            {
                Exit(ex.ToString());
                throw ex;
            }
            Safe safe = DecryptWalletByAskingForPassword(walletFilePath);

            if (Config.connectionType == ConnectionType.Http)
            {
                Dictionary<BitcoinAddress, List<BalanceOperation>> operationsPerAddresses = QBitNinjaJutsus.QueryOperationsPerSafeAddresses(safe, 7);

                // 获取非空私钥
                WriteLine("Finding not empty private keys...");
                var operationsPerNotEmptyPrivateKeys = new Dictionary<BitcoinExtKey, List<BalanceOperation>>();
                foreach (var elem in operationsPerAddresses)
                {
                    var balance = Money.Zero;
                    foreach (var op in elem.Value) balance += op.Amount;
                    if (balance > Money.Zero)
                    {
                        var secret = safe.FindPrivateKey(elem.Key);
                        operationsPerNotEmptyPrivateKeys.Add(secret, elem.Value);
                    }
                }

                // 获取 pubkey 脚本
                WriteLine("Select change address...");
                Script changeScriptPubKey = null;
                Dictionary<BitcoinAddress, List<BalanceOperation>> operationsPerChangeAddresses = QBitNinjaJutsus.QueryOperationsPerSafeAddresses(safe, minUnusedKeys: 1, hdPathType: HdPathType.Change);
                foreach (var elem in operationsPerChangeAddresses)
                {
                    if (elem.Value.Count == 0)
                        changeScriptPubKey = safe.FindPrivateKey(elem.Key).ScriptPubKey;
                }
                if (changeScriptPubKey == null)
                    throw new ArgumentNullException();

                // 获取 UXTO
                WriteLine("Gathering unspent coins...");
                Dictionary<Coin, bool> unspentCoins = QBitNinjaJutsus.GetUnspentCoins(operationsPerNotEmptyPrivateKeys.Keys);

                // 获取费用
                WriteLine("Calculating transaction fee...");
                Money fee;
                try
                {
                    var txSizeInBytes = 250;
                    using (var client = new HttpClient())
                    {

                        const string request = @"https://bitcoinfees.21.co/api/v1/fees/recommended";
                        var result = client.GetAsync(request, HttpCompletionOption.ResponseContentRead).Result;
                        var json = JObject.Parse(result.Content.ReadAsStringAsync().Result);
                        var fastestSatoshiPerByteFee = json.Value<decimal>("fastestFee");
                        fee = new Money(fastestSatoshiPerByteFee * txSizeInBytes, MoneyUnit.Satoshi);
                    }
                }
                catch
                {
                    Exit("Couldn't calculate transaction fee, try it again later.");
                    throw new Exception("Can't get tx fee");
                }
                WriteLine($"Fee: {fee.ToDecimal(MoneyUnit.BTC).ToString("0.#############################")}btc");

                // 有多少 btc 可花费 (有尚未确认的 btc)
                Money availableAmount = Money.Zero;
                Money unconfirmedAvailableAmount = Money.Zero;
                foreach (var elem in unspentCoins)
                {
                    // If can spend unconfirmed add all
                    if (Config.canSpendUnconfirmed)
                    {
                        availableAmount += elem.Key.Amount;
                        if (!elem.Value)
                            unconfirmedAvailableAmount += elem.Key.Amount;
                    }
                    // else only add confirmed ones
                    else
                    {
                        if (elem.Value)
                        {
                            availableAmount += elem.Key.Amount;
                        }
                    }
                }

                // 花费多少
                Money amountToSend = null;
                string amountString = btc;
                if (string.Equals(amountString, "all", StringComparison.OrdinalIgnoreCase))
                {
                    amountToSend = availableAmount;
                    amountToSend -= fee;
                }
                else
                {
                    amountToSend = ParseBtcString(amountString);
                }

                // 检查
                if (amountToSend < Money.Zero || availableAmount < amountToSend + fee)
                    Exit("Not enough coins.");

                decimal feePc = Math.Round((100 * fee.ToDecimal(MoneyUnit.BTC)) / amountToSend.ToDecimal(MoneyUnit.BTC));
                if (feePc > 1)
                {
                    WriteLine();
                    WriteLine($"The transaction fee is {feePc.ToString("0.#")}% of your transaction amount.");
                    WriteLine($"Sending:\t {amountToSend.ToDecimal(MoneyUnit.BTC).ToString("0.#############################")}btc");
                    WriteLine($"Fee:\t\t {fee.ToDecimal(MoneyUnit.BTC).ToString("0.#############################")}btc");
                    ConsoleKey response = GetYesNoAnswerFromUser();
                    if (response == ConsoleKey.N)
                    {
                        Exit("User interruption.");
                    }
                }

                var confirmedAvailableAmount = availableAmount - unconfirmedAvailableAmount;
                var totalOutAmount = amountToSend + fee;
                if (confirmedAvailableAmount < totalOutAmount)
                {
                    var unconfirmedToSend = totalOutAmount - confirmedAvailableAmount;
                    WriteLine();
                    WriteLine($"In order to complete this transaction you have to spend {unconfirmedToSend.ToDecimal(MoneyUnit.BTC).ToString("0.#############################")} unconfirmed btc.");
                    ConsoleKey response = GetYesNoAnswerFromUser();
                    if (response == ConsoleKey.N)
                    {
                        Exit("User interruption.");
                    }
                }

                // 选哪些 UTXO
                WriteLine("Selecting coins...");
                var coinsToSpend = new HashSet<Coin>();
                var unspentConfirmedCoins = new List<Coin>();
                var unspentUnconfirmedCoins = new List<Coin>();
                foreach (var elem in unspentCoins)
                    if (elem.Value) unspentConfirmedCoins.Add(elem.Key);
                    else unspentUnconfirmedCoins.Add(elem.Key);

                bool haveEnough = QBitNinjaJutsus.SelectCoins(ref coinsToSpend, totalOutAmount, unspentConfirmedCoins);
                if (!haveEnough)
                    haveEnough = QBitNinjaJutsus.SelectCoins(ref coinsToSpend, totalOutAmount, unspentUnconfirmedCoins);
                if (!haveEnough)
                    throw new Exception("Not enough funds.");

                // 获取签名密钥
                var signingKeys = new HashSet<ISecret>();
                foreach (var coin in coinsToSpend)
                {
                    foreach (var elem in operationsPerNotEmptyPrivateKeys)
                    {
                        if (elem.Key.ScriptPubKey == coin.ScriptPubKey)
                            signingKeys.Add(elem.Key);
                    }
                }

                // 创建交易
                WriteLine("Signing transaction...");
                var builder = new TransactionBuilder();
                var tx = builder
                    .AddCoins(coinsToSpend)
                    .AddKeys(signingKeys.ToArray())
                    .Send(addressToSend, amountToSend)
                    .SetChange(changeScriptPubKey)
                    .SendFees(fee)
                    .BuildTransaction(true);

                if (!builder.Verify(tx))
                    Exit("Couldn't build the transaction.");

                WriteLine($"Transaction Id: {tx.GetHash()}");

                var qBitClient = new QBitNinjaClient(Config.network);

                // Qbit 相应有 bug, 手动确认		
                BroadcastResponse broadcastResponse;
                var success = false;
                var tried = 0;
                var maxTry = 7;
                do
                {
                    tried++;
                    WriteLine($"Try broadcasting transaction... ({tried})");
                    broadcastResponse = qBitClient.Broadcast(tx).Result;
                    var getTxResp = qBitClient.GetTransaction(tx.GetHash()).Result;
                    if (getTxResp == null)
                    {
                        Thread.Sleep(3000);
                        continue;
                    }
                    else
                    {
                        success = true;
                        break;
                    }
                } while (tried <= maxTry);
                if (!success)
                {
                    if (broadcastResponse.Error != null)
                    {
                        WriteLine($"Error code: {broadcastResponse.Error.ErrorCode} Reason: {broadcastResponse.Error.Reason}");
                    }
                    Exit($"The transaction might not have been successfully broadcasted. Please check the Transaction ID in a block explorer.", ConsoleColor.Blue);
                }
                Exit("Transaction is successfully propagated on the network.", ConsoleColor.Green);
            }
            else if (Config.connectionType == ConnectionType.FullNode)
            {
                throw new NotImplementedException();
            }
            else
            {
                Exit("Invalid connection type.");
            }
        }

        #region Assertions
        public static void AssertWalletNotExists(string walletFilePath)
        {
            if (File.Exists(walletFilePath))
            {
                Exit($"A wallet, named {walletFilePath} already exists.");
            }
        }
        public static void AssertCorrectNetwork(Network network)
        {
            if (network != Config.network)
            {
                WriteLine($"The wallet you want to load is on the {network} Bitcoin network.");
                WriteLine($"But your config file specifies {Config.network} Bitcoin network.");
                Exit();
            }
        }
        public static void AssertCorrectMnemonicFormat(string mnemonic)
        {
            try
            {
                if (new Mnemonic(mnemonic).IsValidChecksum)
                    return;
            }
            catch (FormatException) { }
            catch (NotSupportedException) { }

            Exit("Incorrect mnemonic format.");
        }
        // Inclusive
        #endregion
        #region CommandLineArgumentStuff
        private static string GetWalletFilePath(string path)
        {
            string walletFileName = path;
            if (walletFileName == "") walletFileName = Config.defaultWalletFileName;

            string walletDirName = "Wallets";
            Directory.CreateDirectory(walletDirName);
            return Path.Combine(walletDirName, walletFileName);
        }
        #endregion
        #region CommandLineInterface
        private static Safe DecryptWalletByAskingForPassword(string walletFilePath)
        {
            Safe safe = null;
            string pw;
            bool correctPw = false;
            WriteLine("Type your password:");
            do
            {
                pw = PasswordConsole.ReadPassword();
                try
                {
                    safe = Safe.Load(pw, walletFilePath);
                    AssertCorrectNetwork(safe.Network);
                    correctPw = true;
                }
                catch (System.Security.SecurityException)
                {
                    WriteLine("Invalid password, try again, (or press ctrl+c to exit):");
                    correctPw = false;
                }
            } while (!correctPw);

            if (safe == null)
                throw new Exception("Wallet could not be decrypted.");
            WriteLine($"{walletFilePath} wallet is decrypted.");
            return safe;
        }
        private static ConsoleKey GetYesNoAnswerFromUser()
        {
            ConsoleKey response;
            do
            {
                WriteLine($"Are you sure you want to proceed? (y/n)");
                response = ReadKey(false).Key;   // true is intercept key (dont show), false is show
                if (response != ConsoleKey.Enter)
                    WriteLine();
            } while (response != ConsoleKey.Y && response != ConsoleKey.N);
            return response;
        }

        public static void Exit(string reason = "", ConsoleColor color = ConsoleColor.Red)
        {
            ForegroundColor = color;
            WriteLine();
            if (reason != "")
            {
                WriteLine(reason);
            }
            WriteLine("Press any key to exit...");
            ResetColor();
            ReadKey();
            Environment.Exit(0);
        }
        #endregion
        #region Helpers
        private static Money ParseBtcString(string value)
        {
            decimal amount;
            if (!decimal.TryParse(
                        value.Replace(',', '.'),
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out amount))
            {
                Exit("Wrong btc amount format.");
            }


            return new Money(amount, MoneyUnit.BTC);
        }
        #endregion
    }
}