﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace FateGrandAutomata
{
    public class Support
    {
        const int CraftEssenceHeight = 90;

        const string LimitBrokenCharacter = "*";

        readonly List<string> _preferredServantArray = new List<string>(),
            _friendNameArray = new List<string>();

        readonly List<(string Name, bool PreferMlb)> _preferredCraftEssenceTable = new List<(string Name, bool PreferMlb)>();

        public void Init()
        {
            static IEnumerable<string> Split(string Str)
            {
                foreach (var s in Str.Split(','))
                {
                    var val = s.Trim();

                    if (val.ToLower() != "any")
                    {
                        yield return val;
                    }
                }
            }

            // Friend names
            foreach (var friend in Split(Preferences.Support.FriendNames))
            {
                _friendNameArray.Add(friend.Trim());
            }

            // Servants
            foreach (var servant in Split(Preferences.Support.PreferredServants))
            {
                _preferredServantArray.Add(servant.Trim());
            }

            // Craft essences
            foreach (var craftEssence in Split(Preferences.Support.PreferredCEs))
            {
                _preferredCraftEssenceTable.Add((
                    craftEssence.Replace(LimitBrokenCharacter, ""),
                    craftEssence.StartsWith(LimitBrokenCharacter)));
            }
        }

        public bool SelectSupport(SupportSelectionMode SelectionMode)
        {
            var pattern = ImageLocator.SupportScreen;
            while (!Game.SupportScreenRegion.Exists(pattern)) { }

            switch (SelectionMode)
            {
                case SupportSelectionMode.First:
                    return SelectFirst();

                case SupportSelectionMode.Manual:
                    SelectManual();
                    break;

                case SupportSelectionMode.Friend:
                    return SelectFriend();

                case SupportSelectionMode.Preferred:
                    var searchmethod = DecideSearchMethod();
                    return SelectPreferred(searchmethod);

                default:
                    throw new ArgumentException("Invalid support selection mode");
            }

            return false;
        }

        void SelectManual()
        {
            // TODO: Pause for manual support selection.
            throw new NotImplementedException();
        }

        bool SelectFirst()
        {
            Game.Wait(1);
            Game.SupportFirstSupportClick.Click();

            var pattern = ImageLocator.SupportScreen;

            // https://github.com/29988122/Fate-Grand-Order_Lua/issues/192 , band-aid fix but it's working well.
            if (Game.SupportScreenRegion.Exists(pattern))
            {
                Game.Wait(2);

                while (Game.SupportScreenRegion.Exists(pattern))
                {
                    Game.Wait(10);
                    Game.SupportUpdateClick.Click();
                    Game.Wait(1);
                    Game.SupportUpdateYesClick.Click();
                    Game.Wait(3);
                    Game.SupportFirstSupportClick.Click();
                    Game.Wait(1);
                }
            }

            return true;
        }

        (SupportSearchResult Result, Region Support) SearchVisible(SearchFunction SearchMethod)
        {
            (SupportSearchResult Result, Region Support) PerformSearch()
            {
                if (!IsFriend(Game.SupportFriendRegion))
                {
                    // no friends on screen, so there's no point in scrolling anymore
                    return (SupportSearchResult.NoFriendsFound, null);
                }

                var (support, bounds) = SearchMethod();

                if (support == null)
                {
                    // nope, not found this time. keep scrolling
                    return (SupportSearchResult.NotFound, null);
                }

                // bounds are already returned by searchMethod.byServantAndCraftEssence, but not by the other methods
                bounds ??= FindSupportBounds(support);

                if (!IsFriend(bounds))
                {
                    // found something, but it doesn't belong to a friend. keep scrolling
                    return (SupportSearchResult.NotFound, null);
                }

                return (SupportSearchResult.Found, support);
            }

            return Game.Impl.UseSameSnapIn(PerformSearch);
        }

        bool SelectFriend()
        {
            if (_friendNameArray.Count > 0)
            {
                return SelectPreferred(() => (FindFriendName(), null));
            }

            throw new ArgumentException("When using 'friend' support selection mode, specify at least one friend name.");
        }

        bool SelectPreferred(SearchFunction SearchMethod)
        {
            var numberOfSwipes = 0;
            var numberOfUpdates = 0;

            while (true)
            {
                var (result, support) = SearchVisible(SearchMethod);

                if (result == SupportSearchResult.Found)
                {
                    support.Click();
                    return true;
                }

                if (result == SupportSearchResult.NotFound && numberOfSwipes < Preferences.Support.SwipesPerUpdate)
                {
                    ScrollList();
                    ++numberOfSwipes;
                    Game.Wait(0.3);
                }

                else if (numberOfUpdates < Preferences.Support.MaxUpdates)
                {
                    Game.Impl.Toast("Support list will be updated in 3 seconds.");
                    Game.Wait(3);

                    Game.SupportUpdateClick.Click();
                    Game.Wait(1);
                    Game.SupportUpdateYesClick.Click();
                    Game.Wait(3);

                    ++numberOfUpdates;
                    numberOfSwipes = 0;
                }

                else
                {
                    // -- okay, we have run out of options, let's give up
                    Game.SupportListTopClick.Click();
                    return SelectSupport(Preferences.Support.FallbackTo);
                }
            }
        }

        SearchFunction DecideSearchMethod()
        {
            var hasServants = _preferredServantArray.Count > 0;
            var hasCraftEssences = _preferredCraftEssenceTable.Count > 0;

            if (hasServants && hasCraftEssences)
            {
                return () =>
                {
                    var servants = FindServants();

                    foreach (var servant in servants)
                    {
                        var supportBounds = FindSupportBounds(servant);
                        var craftEssence = FindCraftEssence(supportBounds);

                        // CEs are always below Servants in the support list
                        // see docs/support_list_edge_case_fix.png to understand why this conditional exists
                        if (craftEssence != null && craftEssence.Y > servant.Y)
                        {
                            // only return if found. if not, try the other servants before scrolling
                            return (craftEssence, supportBounds);
                        }
                    }

                    // not found, continue scrolling
                    return (null, null);
                };
            }

            if (hasServants)
            {
                return () => (FindServants().FirstOrDefault(), null);
            }

            if (hasCraftEssences)
            {
                return () => (FindCraftEssence(Game.SupportListRegion), null);
            }

            throw new ArgumentException("When using 'preferred' support selection mode, specify at least one Servant or Craft Essence.");
        }

        void ScrollList()
        {
            Game.Impl.Scroll(Game.SupportSwipeStartClick, Game.SupportSwipeEndClick);
        }

        Region FindFriendName()
        {
            foreach (var friendName in _friendNameArray)
            {
                foreach (var theFriend in Game.Impl.FindAll(Game.SupportFriendsRegion, ImageLocator.LoadSupportImagePattern(friendName)))
                {
                    return theFriend;
                }
            }

            return null;
        }

        IEnumerable<Region> FindServants()
        {
            foreach (var preferredServant in _preferredServantArray)
            {
                foreach (var servant in Game.Impl.FindAll(Game.SupportListRegion, ImageLocator.LoadSupportImagePattern(preferredServant)))
                {
                    yield return servant;
                }
            }
        }

        Region FindCraftEssence(Region SearchRegion)
        {
            foreach (var preferredCraftEssence in _preferredCraftEssenceTable)
            {
                var craftEssences = Game.Impl.FindAll(SearchRegion, ImageLocator.LoadSupportImagePattern(preferredCraftEssence.Name));

                foreach (var craftEssence in craftEssences)
                {
                    if (!preferredCraftEssence.PreferMlb || IsLimitBroken(craftEssence))
                    {
                        return craftEssence;
                    }
                }
            }

            return null;
        }

        Region FindSupportBounds(Region Support)
        {
            var supportBound = new Region(76, 0, 2356, 428);
            var regionAnchor = ImageLocator.SupportRegionTool;
            var regionArray = Game.Impl.FindAll(new Region(1670, 0, 90, 1440), regionAnchor);
            var defaultRegion = supportBound;

            foreach (var testRegion in regionArray)
            {
                supportBound.Y = testRegion.Y - 156;

                if (supportBound.Contains(Support))
                {
                    return supportBound;
                }
            }

            Game.Impl.Toast("Default Region being returned; file an issue on the github for this issue");
            return defaultRegion;
        }

        bool IsFriend(Region Region)
        {
            var friendPattern = ImageLocator.Friend;

            return !Preferences.Support.FriendsOnly || Region.Exists(friendPattern);
        }

        bool IsLimitBroken(Region CraftEssence)
        {
            var limitBreakRegion = Game.SupportLimitBreakRegion;
            limitBreakRegion.Y = CraftEssence.Y;

            var limitBreakPattern = ImageLocator.LimitBroken;

            return limitBreakRegion.Exists(limitBreakPattern);
        }
    }
}