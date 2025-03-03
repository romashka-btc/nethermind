// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core;
using Nethermind.Core.Crypto;

namespace Nethermind.Blockchain.Receipts
{
    public interface IReceiptFinder
    {
        Hash256? FindBlockHash(Hash256 txHash);
        TxReceipt[] Get(Block block, bool recover = true, bool recoverSender = true);
        TxReceipt[] Get(Hash256 blockHash, bool recover = true);
        bool CanGetReceiptsByHash(long blockNumber);
        bool TryGetReceiptsIterator(long blockNumber, Hash256 blockHash, out ReceiptsIterator iterator);
    }
}
