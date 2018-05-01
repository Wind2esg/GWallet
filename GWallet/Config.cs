using System;
using System.IO;
using Newtonsoft.Json;
using NBitcoin;

namespace GWallet
{
    // 连接方式有 FullNode, SPV, Http. 在此仅使用 Http。
    public enum ConnectionType
    {
        FullNode,
        Http
    }
    public static class Config
    {
        public static string defaultWalletFileName = @"Wallet.json";
        public static Network network = Network.Main;
        public static ConnectionType connectionType = ConnectionType.Http;
        public static bool canSpendUnconfirmed = false;

        // 如果不存在 Config.json 则创建，否则，从配置文件中加载数据。
        static Config()
        {
            if (!File.Exists(ConfigFileSerializer.ConfigFilePath))
            {
                Save();
                Console.WriteLine($"{ConfigFileSerializer.ConfigFilePath} was missing. It has been created created with default settings.");
            }
            Load();
        }

        public static void Load()
        {
            var rawContent = ConfigFileSerializer.Deserialize();

            defaultWalletFileName = rawContent.DefaultWalletFileName;
            network = rawContent.Network == Network.Main.ToString() ? Network.Main : Network.TestNet;
            connectionType = rawContent.ConnectionType == ConnectionType.FullNode.ToString() ? ConnectionType.FullNode : ConnectionType.Http;
            canSpendUnconfirmed = rawContent.CanSpendUnconfirmed == "True" ? true : false;
        }
        public static void Save()
        {
            ConfigFileSerializer.Serialize(defaultWalletFileName, network.ToString(), connectionType.ToString(), canSpendUnconfirmed.ToString());
            Load();
        }
    }

    // 使用 Newtonsoft.json 处理 json 序列化
    public class ConfigFileSerializer
    {
        public static string ConfigFilePath = "Config.json";
        public string DefaultWalletFileName { get; set; }
        public string Network { get; set; }
        public string ConnectionType { get; set; }
        public string CanSpendUnconfirmed { get; set; }

        [JsonConstructor]
        private ConfigFileSerializer(string walletFileName, string network, string connectionType, string canSpendUnconfirmed)
        {
            DefaultWalletFileName = walletFileName;
            Network = network;
            ConnectionType = connectionType;
            CanSpendUnconfirmed = canSpendUnconfirmed;
        }

        internal static void Serialize(string walletFileName, string network, string connectionType, string canSpendUnconfirmed)
        {
            string content = JsonConvert.SerializeObject(new ConfigFileSerializer(walletFileName, network, connectionType, canSpendUnconfirmed), Formatting.Indented);

            File.WriteAllText(ConfigFilePath, content);
        }

        internal static ConfigFileSerializer Deserialize()
        {
            // 反序列化时，如果配置文件不存在，则创建默认配置的配置文件
            if (!File.Exists(ConfigFilePath))
            {
                Config.Save();
                Console.WriteLine($"{ConfigFilePath} was missing. It has been created created with default settings.");
            }

            string contentString = File.ReadAllText(ConfigFilePath);
            ConfigFileSerializer configFileSerializer = JsonConvert.DeserializeObject<ConfigFileSerializer>(contentString);
            return configFileSerializer;
        }
    }
}