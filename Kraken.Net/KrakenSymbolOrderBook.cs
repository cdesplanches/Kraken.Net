﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.OrderBook;
using CryptoExchange.Net.Sockets;
using Kraken.Net.Interfaces;
using Kraken.Net.Objects;
using Kraken.Net.Objects.Socket;

namespace Kraken.Net
{
    /// <summary>
    /// Live order book implementation
    /// </summary>
    public class KrakenSymbolOrderBook : SymbolOrderBook
    {
        private readonly IKrakenSocketClient socketClient;
        private bool initialSnapshotDone;
        private readonly int limit;

        /// <summary>
        /// Create a new order book instance
        /// </summary>
        /// <param name="market">The symbol the order book is for</param>
        /// <param name="limit">The initial limit of entries in the order book</param>
        /// <param name="options">Options for the order book</param>
        public KrakenSymbolOrderBook(string market, int limit, KrakenOrderBookOptions options = null) : base(market, options ?? new KrakenOrderBookOptions())
        {
            socketClient = options?.SocketClient ?? new KrakenSocketClient();

            this.limit = limit;
        }

        /// <inheritdoc />
        protected override async Task<CallResult<UpdateSubscription>> DoStart()
        {
            var result = await socketClient.SubscribeToDepthUpdatesAsync(Symbol, limit, ProcessUpdate).ConfigureAwait(false);
            if (!result.Success)
                return result;

            Status = OrderBookStatus.Syncing;

            while (!initialSnapshotDone)
                await Task.Delay(10).ConfigureAwait(false); // Wait for first update to fill the order book

            return result;
        }

        /// <inheritdoc />
        protected override void DoReset()
        {
            initialSnapshotDone = false;
        }

        private void ProcessUpdate(KrakenSocketEvent<KrakenStreamOrderBook> data)
        {
            if (!initialSnapshotDone)
            {
                SetInitialOrderBook(DateTime.UtcNow.Ticks, data.Data.Asks, data.Data.Bids);
                initialSnapshotDone = true;
            }
            else
            {
                var processEntries = new List<ProcessEntry>();
                foreach (var entry in data.Data.Asks)
                    processEntries.Add(new ProcessEntry(OrderBookEntryType.Ask, new OrderBookEntry(entry.Price, entry.Quantity)));
                foreach (var entry in data.Data.Bids)
                    processEntries.Add(new ProcessEntry(OrderBookEntryType.Bid, new OrderBookEntry(entry.Price, entry.Quantity)));
                
                UpdateOrderBook(DateTime.UtcNow.Ticks, DateTime.UtcNow.Ticks, processEntries);
            }
        }

        /// <inheritdoc />
        protected override async Task<CallResult<bool>> DoResync()
        {
            while (!initialSnapshotDone)
                await Task.Delay(10).ConfigureAwait(false); // Wait for first update to fill the order book

            return new CallResult<bool>(true, null);
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public override void Dispose()
        {
            processBuffer.Clear();
            asks.Clear();
            bids.Clear();

            socketClient?.Dispose();
        }
    }
}