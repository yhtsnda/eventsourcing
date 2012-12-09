﻿//Copyright (c) CodeSharp.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;

namespace CodeSharp.EventSourcing
{
    /// <summary>
    /// Default implementation of subscription store, use ADO.NET to persist event subscription.
    /// </summary>
    public class DefaultSubscriptionStore : ISubscriptionStore
    {
        private IDbConnectionFactory _connectionFactory;
        private InMemorySubscriptionStore _memoryStore;
        private Timer _refreshSubscriptionTimer;
        private ILogger _logger;
        private const int RefreshPeriod = 30 * 1000; //默认30秒刷新一次订阅者信息

        public DefaultSubscriptionStore(IDbConnectionFactory connectionFactory, ILoggerFactory loggerFactory)
        {
            _connectionFactory = connectionFactory;
            _logger = loggerFactory.Create("EventSourcing.DefaultSubscriptionStore");
            _memoryStore = new InMemorySubscriptionStore();
            _refreshSubscriptionTimer = new Timer((x) => RefreshSubscriptions(), null, 0, RefreshPeriod);
        }

        public void Subscribe(Address address, Type messageType)
        {
            using (var connection = _connectionFactory.OpenConnection())
            {
                var subscriberAddress = address.ToString();
                var messageTypeName = messageType.AssemblyQualifiedName;
                var table = Configuration.Instance.GetSetting<string>("subscriptionTable");
                var count = connection.GetCount(new { SubscriberAddress = subscriberAddress, MessageType = messageTypeName }, table);
                if (count == 0)
                {
                    connection.Insert(new { UniqueId = Guid.NewGuid(), SubscriberAddress = subscriberAddress, MessageType = messageTypeName }, table);
                    _logger.DebugFormat("Subscriber '{0}' subscribes message '{1}'.", subscriberAddress, messageTypeName);
                }
            }
            _memoryStore.Subscribe(address, messageType);
        }
        public void ClearAddressSubscriptions(Address address)
        {
            using (var connection = _connectionFactory.OpenConnection())
            {
                var subscriberAddress = address.ToString();
                var table = Configuration.Instance.GetSetting<string>("subscriptionTable");
                connection.Delete(new { SubscriberAddress = subscriberAddress }, table);
                _logger.DebugFormat("Cleaned up subscriptions of subscriber address '{0}'", subscriberAddress);
            }
            _memoryStore.ClearAddressSubscriptions(address);
        }
        public void Unsubscribe(Address address, Type messageType)
        {
            using (var connection = _connectionFactory.OpenConnection())
            {
                var subscriberAddress = address.ToString();
                var messageTypeName = messageType.AssemblyQualifiedName;
                var table = Configuration.Instance.GetSetting<string>("subscriptionTable");
                connection.Delete(new { SubscriberAddress = subscriberAddress, MessageType = messageTypeName }, table);
                _logger.DebugFormat("Subscriber '{0}' unsubscribes message '{1}'.", subscriberAddress, messageTypeName);
            }
            _memoryStore.Unsubscribe(address, messageType);
        }
        public IEnumerable<Address> GetSubscriberAddressesForMessage(Type messageType)
        {
            return _memoryStore.GetSubscriberAddressesForMessage(messageType);
        }

        private void RefreshSubscriptions()
        {
            using (var connection = _connectionFactory.OpenConnection())
            {
                var table = Configuration.Instance.GetSetting<string>("subscriptionTable");
                var subscriptions = connection.QueryAll(table);
                var memoryStore = new InMemorySubscriptionStore();

                foreach (var subscription in subscriptions)
                {
                    var address = Address.Parse(subscription.SubscriberAddress as string);
                    var messageType = Type.GetType(subscription.MessageType as string);
                    if (messageType != null)
                    {
                        memoryStore.Subscribe(address, messageType);
                    }
                }

                _memoryStore = memoryStore;
            }
        }
    }
}
