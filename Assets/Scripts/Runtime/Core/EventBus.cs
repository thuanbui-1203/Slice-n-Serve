using System;
using System.Collections.Generic;

namespace SliceNServe.Core
{
    /// <summary>Typed publish/subscribe over struct events. Deliberately tiny — no DI framework
    /// is needed for Section 2 (plan §7).</summary>
    public sealed class EventBus
    {
        private readonly Dictionary<Type, Delegate> _handlers = new Dictionary<Type, Delegate>();

        public void Subscribe<T>(Action<T> handler) where T : struct
        {
            _handlers.TryGetValue(typeof(T), out Delegate existing);
            _handlers[typeof(T)] = Delegate.Combine(existing, handler);
        }

        public void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            if (!_handlers.TryGetValue(typeof(T), out Delegate existing)) return;

            Delegate remaining = Delegate.Remove(existing, handler);
            if (remaining == null) _handlers.Remove(typeof(T));
            else _handlers[typeof(T)] = remaining;
        }

        public void Publish<T>(in T evt) where T : struct
        {
            if (!_handlers.TryGetValue(typeof(T), out Delegate existing)) return;
            (existing as Action<T>)?.Invoke(evt);
        }
    }
}
