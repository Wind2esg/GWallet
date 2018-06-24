# GWallet

## C# HD Wallet for Bitcoin 

## API


| Method | Return | Param |		
| - | :-: | -: |
| GenerateWallet | void | string path |
| RecoverWallet | void | string path |
| ShowBalances | void | string path |
| ShowHistory | void | string path |
| Receive | void | string path |
| ShowHistSendory | void | string address, string btc, string path |
| GetWalletFilePath | string | string path |
| AssertWalletNotExists | void | string walletFilePath |
| AssertCorrectNetwork | void | Network network |
| AssertCorrectMnemonicFormat | void | string mnemonic |
