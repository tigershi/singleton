﻿/*
 * Copyright 2020-2021 VMware, Inc.
 * SPDX-License-Identifier: EPL-2.0
 */

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SingletonClient.Implementation.Support.ByKey
{
    public interface ISingletonByKeyRelease
    {
        string GetSourceLocale();
        ISingletonByKeyLocale GetLocaleItem(string locale, bool asSource);
        int GetComponentIndex(string component);
        int GetKeyCountInComponent(int componentIndex, ISingletonByKeyLocale localeItem);
        ICollection<string> GetKeysInComponent(int componentIndex, ISingletonByKeyLocale localeItem);
        string GetString(string key, int componentIndex, ISingletonByKeyLocale localeItem, bool needFallback = false);
        bool SetString(string key, ISingletonComponent componentObject, int componentIndex,
            ISingletonByKeyLocale localeItem, string message);
        SingletonByKeyItem GetKeyItem(int pageIndex, int indexInPage);
    }

    public class SingletonByKeyRelease : ISingletonByKeyRelease
    {
        public const int PAGE_MAX_SIZE = 1024;
        public const int COMPONENT_PAGE_MAX_SIZE = 128;

        private readonly string _localeSource;
        private readonly string _localeDefault;
        private readonly bool _isDifferent;
        private readonly bool _onlyByKey;
        private readonly ISingletonRelease _release;
        private readonly SingletonByKeyComponents _compentTable;

        private readonly ConcurrentDictionary<string, SingletonByKeyItem> _keyAttrTable;
        private readonly SingletonByKeyTable<SingletonByKeyItem> _items;

        private readonly Hashtable _locales;
        private readonly Hashtable _sources;
        private int _itemCount = 0;

        private ISingletonByKeyLocale _sourceLocal;
        private ISingletonByKeyLocale _sourceRemote;
        private ISingletonByKeyLocale _defaultRemote;

        private readonly object _lockObject = new object();

        public SingletonByKeyRelease(ISingletonRelease release, string localeSource, string localeDefault,
            bool isDifferent, string cacheType)
        {
            _release = release;
            _localeSource = localeSource;
            _onlyByKey = string.Compare(ConfigConst.CacheByKey, cacheType, StringComparison.InvariantCultureIgnoreCase) == 0;

            _compentTable = new SingletonByKeyComponents();

            _keyAttrTable = new ConcurrentDictionary<string, SingletonByKeyItem>(StringComparer.InvariantCultureIgnoreCase);
            _items = new SingletonByKeyTable<SingletonByKeyItem>(SingletonByKeyRelease.PAGE_MAX_SIZE);

            _locales = SingletonUtil.NewHashtable(true);
            _sources = SingletonUtil.NewHashtable(true);

            _isDifferent = isDifferent;
            _localeDefault = localeDefault;
        }

        public bool SetItem(SingletonByKeyItem item, int pageIndex, int indexInPage)
        {
            SingletonByKeyItem[] array = _items.GetPage(pageIndex);
            if (array == null)
            {
                array = _items.NewPage(pageIndex);
            }

            array[indexInPage] = item;
            return true;
        }

        public int GetAndAddItemCount()
        {
            int count = _itemCount;
            _itemCount++;
            return count;
        }

        /// <summary>
        /// ISingletonByKeyRelease
        /// </summary>
        public string GetSourceLocale()
        {
            return _localeSource;
        }

        /// <summary>
        /// ISingletonByKeyRelease
        /// </summary>
        public ISingletonByKeyLocale GetLocaleItem(string locale, bool asSource)
        {
            Hashtable table = asSource ? _sources : _locales;
            ISingletonByKeyLocale item = (ISingletonByKeyLocale)table[locale];
            if (item == null)
            {
                ISingletonLocale singletonLocale = SingletonUtil.GetSingletonLocale(locale);
                for(int i=1; i<singletonLocale.GetCount(); i++)
                {
                    string one = singletonLocale.GetNearLocale(i);
                    if (table[one] != null)
                    {
                        table[locale] = table[one];
                        return (ISingletonByKeyLocale)table[one];
                    }
                }

                item = new SingletonByKeyLocale(this, locale, asSource);
                for (int i = 0; i < singletonLocale.GetCount(); i++)
                {
                    string one = singletonLocale.GetNearLocale(i);
                    table[one] = item;
                }
            }
            return item;
        }

        /// <summary>
        /// ISingletonByKeyRelease
        /// </summary>
        public int GetComponentIndex(string component)
        {
            return _compentTable.GetId(component);
        }

        /// <summary>
        /// ISingletonByKeyRelease
        /// </summary>
        public int GetKeyCountInComponent(int componentIndex, ISingletonByKeyLocale localeItem)
        {
            int count = 0;
            if (localeItem != null)
            {
                foreach (var pair in _keyAttrTable)
                {
                    SingletonByKeyItem item = pair.Value;
                    if (item.ComponentIndex == componentIndex)
                    {
                        string message;
                        localeItem.GetMessage(componentIndex, item.PageIndex, item.IndexInPage, out message);
                        if (message != null)
                        {
                            count++;
                        }
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// ISingletonByKeyRelease
        /// </summary>
        public ICollection<string> GetKeysInComponent(int componentIndex, ISingletonByKeyLocale localeItem)
        {
            List<string> array = new List<string>();
            if (localeItem != null)
            {
                foreach (var pair in _keyAttrTable)
                {
                    SingletonByKeyItem item = pair.Value;
                    if (item.ComponentIndex == componentIndex)
                    {
                        string message;
                        localeItem.GetMessage(componentIndex, item.PageIndex, item.IndexInPage, out message);
                        if (message != null)
                        {
                            array.Add(pair.Key);
                        }
                    }
                }
            }
            return array;
        }

        /// <summary>
        /// ISingletonByKeyRelease
        /// </summary>
        public string GetString(string key, int componentIndex, ISingletonByKeyLocale localeItem, bool needFallback = false)
        {
            if (componentIndex < 0 && !_onlyByKey)
            {
                return null;
            }

            SingletonByKeyItem item;
            _keyAttrTable.TryGetValue(key, out item);

            if (componentIndex >= 0)
            {
                while (item != null)
                {
                    if (item.ComponentIndex == componentIndex)
                    {
                        break;
                    }
                    item = item.Next;
                }
            }
            if (item == null)
            {
                return null;
            }

            string message = null;
            if (!needFallback)
            {
                if (localeItem.GetMessage(componentIndex, item.PageIndex, item.IndexInPage, out message))
                {
                    return message;
                }
                return null;
            }

            if ((item.SourceStatus & 0x01) == 0x01)
            {
                bool success = localeItem.GetMessage(
                    componentIndex, item.PageIndex, item.IndexInPage, out message);
                if (success)
                {
                    return message;
                }

                if (_isDifferent)
                {
                    if (_defaultRemote == null)
                    {
                        _defaultRemote = this.GetLocaleItem(_localeDefault, false);
                    }
                    if (_defaultRemote.GetMessage(componentIndex, item.PageIndex, item.IndexInPage, out message))
                    {
                        return message;
                    }
                }
            }

            if (message == null)
            {
                if ((item.SourceStatus & 0x04) == 0x04)
                {
                    if (_sourceLocal.GetMessage(componentIndex, item.PageIndex, item.IndexInPage, out message))
                    {
                        return message;
                    }
                }
                else if ((item.SourceStatus & 0x02) == 0x02)
                {
                    bool success = _sourceRemote.GetMessage(
                        componentIndex, item.PageIndex, item.IndexInPage, out message);
                    if (success)
                    {
                        return message;
                    }
                }
            }

            return message;
        }

        private SingletonByKeyItem NewKeyItem(int componentIndex)
        {
            int itemIndex = GetAndAddItemCount();
            SingletonByKeyItem item = new SingletonByKeyItem(componentIndex, itemIndex);
            SetItem(item, item.PageIndex, item.IndexInPage);
            return item;
        }

        private void FindOrAdd(SingletonByKeyLookup lookup)
        {
            SingletonByKeyItem item;
            bool found = _keyAttrTable.TryGetValue(lookup.Key, out item);
            if (!found || item == null)
            {
                lookup.CurrentItem = NewKeyItem(lookup.ComponentIndex);
                lookup.Add = 1;
                return;
            }

            while (item != null)
            {
                if (item.ComponentIndex == lookup.ComponentIndex)
                {
                    lookup.CurrentItem = item;
                    return;
                }
                lookup.AboveItem = item;
                item = item.Next;
            }

            lookup.CurrentItem = NewKeyItem(lookup.ComponentIndex);
            lookup.Add = 2;
        }

        private bool DoSetString(string key, ISingletonComponent componentObject, int componentIndex, 
            ISingletonByKeyLocale localeItem, string message)
        {
            SingletonByKeyLookup lookup = new SingletonByKeyLookup(key, componentIndex, message);
            this.FindOrAdd(lookup);

            SingletonByKeyItem item = lookup.CurrentItem;
            if (item == null)
            {
                return false;
            }

            bool done = localeItem.SetMessage(message, componentObject, componentIndex, item.PageIndex, item.IndexInPage);
            if (done && localeItem.IsSourceLocale())
            {
                byte status = item.SourceStatus;
                if (localeItem.IsSource())
                {
                    _sourceLocal = localeItem;
                    status |= 0x04;
                }
                else if (localeItem.IsSourceLocale())
                {
                    _sourceRemote = localeItem;
                    status |= 0x02;
                }
                if ((status & 0x06) != 0x06)
                {
                    status |= 0x01;
                }
                else
                {
                    string localSource;
                    _sourceLocal.GetMessage(componentIndex, item.PageIndex, item.IndexInPage, out localSource);
                    string remoteSource;
                    _sourceRemote.GetMessage(componentIndex, item.PageIndex, item.IndexInPage, out remoteSource);
                    if (String.Equals(localSource, remoteSource))
                    {
                        status |= 0x01;
                    }
                    else
                    {
                        status &= 0x06;
                    }
                }
                item.SourceStatus = status;
            }

            // Finally, it's added in the table after it has been prepared.
            if (lookup.Add == 1)
            {
                _keyAttrTable[key] = lookup.CurrentItem;
            }
            else if (lookup.Add == 2)
            {
                lookup.AboveItem.Next = lookup.CurrentItem;
            }
            return done;
        }

        /// <summary>
        /// ISingletonByKeyRelease
        /// </summary>
        public bool SetString(string key, ISingletonComponent componentObject, int componentIndex,
            ISingletonByKeyLocale localeItem, string message)
        {
            if (message == null || key == null || localeItem == null)
            {
                return false;
            }
            string text = this.GetString(key, componentIndex, localeItem);
            if (message.Equals(text))
            {
                return false;
            }

            lock (_lockObject)
            {
                text = this.GetString(key, componentIndex, localeItem);
                if (message.Equals(text))
                {
                    return false;
                }

                return this.DoSetString(key, componentObject, componentIndex, localeItem, message);
            }
        }

        /// <summary>
        /// ISingletonByKeyRelease
        /// </summary>
        public SingletonByKeyItem GetKeyItem(int pageIndex, int indexInPage)
        {
            SingletonByKeyItem[] array = _items.GetPage(pageIndex);
            if (array == null)
            {
                return null;
            }

            return array[indexInPage];
        }
    }
}
