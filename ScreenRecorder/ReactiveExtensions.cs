using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace ScreenRecorder
{
    internal static class ReactiveExtensions
    {
        private static readonly Dictionary<INotifyPropertyChanged, SubscriptionSet> Subscriptions
            = new Dictionary<INotifyPropertyChanged, SubscriptionSet>();

        private static readonly object SyncLock = new object();

        public static void WhenChanged(this INotifyPropertyChanged publisher, Action callback, params string[] propertyNames)
        {
            var bindPropertyChanged = false;

            lock (SyncLock)
            {
                if (Subscriptions.ContainsKey(publisher) == false)
                {
                    Subscriptions[publisher] = new SubscriptionSet();
                    bindPropertyChanged = true;
                }

                foreach (var propertyName in propertyNames)
                {
                    if (Subscriptions[publisher].ContainsKey(propertyName) == false)
                        Subscriptions[publisher][propertyName] = new CallbackList();
                    Subscriptions[publisher][propertyName].Add(callback);
                }
            }

            callback();

            if (bindPropertyChanged == false)
                return;

            publisher.PropertyChanged += (s, e) =>
            {
                CallbackList propertyCallbacks = null;

                lock (SyncLock)
                {
                    if (Subscriptions[publisher].ContainsKey(e.PropertyName) == false)
                        return;
                    propertyCallbacks = Subscriptions[publisher][e.PropertyName];
                }

                foreach (var propertyCallback in propertyCallbacks)
                {
                    propertyCallback.Invoke();
                }
            };
        }

        internal sealed class SubscriptionSet : Dictionary<string, CallbackList> { }

        internal sealed class CallbackList : List<Action>
        {
            public CallbackList()
                : base(32) { }
        }
    }
}
