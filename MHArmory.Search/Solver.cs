﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MHArmory.Core;
using MHArmory.Core.DataStructures;

namespace MHArmory.Search
{
    public class Solver
    {
        private readonly ISolverData data;

        public Solver(ISolverData data)
        {
            this.data = data;
        }

        public event Action<string> DebugData;

        public Task<IList<ArmorSetSearchResult>> SearchArmorSets()
        {
            return SearchArmorSets(CancellationToken.None);
        }

        public Task<IList<ArmorSetSearchResult>> SearchArmorSets(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                return SearchArmorSetsInternal(
                    data.DesiredAbilities,
                    cancellationToken
                );
            });
        }

        private readonly ObjectPool<List<ArmorSetJewelResult>> jewelResultObjectPool = new ObjectPool<List<ArmorSetJewelResult>>(() => new List<ArmorSetJewelResult>());
        private readonly ObjectPool<int[]> availableSlotsObjectPool = new ObjectPool<int[]>(() => new int[3]);
        private readonly ObjectPool<Dictionary<IArmorSet, int>> armorSetsObjectPool = new ObjectPool<Dictionary<IArmorSet, int>>(() => new Dictionary<IArmorSet, int>(ArmorSetEqualityComparer.Default));
        private readonly ObjectPool<IEquipment[]> searchEquipmentsObjectPool = new ObjectPool<IEquipment[]>(() => new IEquipment[6]);

        private async Task<IList<ArmorSetSearchResult>> SearchArmorSetsInternal(
            IEnumerable<IAbility> desiredAbilities,
            CancellationToken cancellationToken
        )
        {
            var sw = Stopwatch.StartNew();

            if (cancellationToken.IsCancellationRequested)
                return null;

            var allCharms = new List<ICharmLevel>();

            if (cancellationToken.IsCancellationRequested)
                return null;

            var heads = new List<IArmorPiece>();
            var chests = new List<IArmorPiece>();
            var gloves = new List<IArmorPiece>();
            var waists = new List<IArmorPiece>();
            var legs = new List<IArmorPiece>();

            var test = new List<ArmorSetSearchResult>();

            var generator = new EquipmentCombinationGenerator(
                searchEquipmentsObjectPool,
                data.AllHeads.Where(x => x.IsSelected).Select(x => x.Equipment),
                data.AllChests.Where(x => x.IsSelected).Select(x => x.Equipment),
                data.AllGloves.Where(x => x.IsSelected).Select(x => x.Equipment),
                data.AllWaists.Where(x => x.IsSelected).Select(x => x.Equipment),
                data.AllLegs.Where(x => x.IsSelected).Select(x => x.Equipment),
                data.AllCharms.Where(x => x.IsSelected).Select(x => x.Equipment)
            );

            var sb = new StringBuilder();

            long hh = data.AllHeads.Count(x => x.IsSelected);
            long cc = data.AllChests.Count(x => x.IsSelected);
            long gg = data.AllGloves.Count(x => x.IsSelected);
            long ww = data.AllWaists.Count(x => x.IsSelected);
            long ll = data.AllLegs.Count(x => x.IsSelected);
            long ch = data.AllCharms.Count(x => x.IsSelected);

            var nfi = new System.Globalization.NumberFormatInfo
            {
                NumberGroupSeparator = "'"
            };

            sb.AppendLine($"Heads count:  {hh}");
            sb.AppendLine($"Chests count: {cc}");
            sb.AppendLine($"Gloves count: {gg}");
            sb.AppendLine($"Waists count: {ww}");
            sb.AppendLine($"Legs count:   {ll}");
            sb.AppendLine($"Charms count:   {ch}");
            sb.AppendLine("-----");
            sb.AppendLine($"Min sLot size: {data.MinJewelSize}");
            sb.AppendLine($"Max sLot size: {data.MaxJewelSize}");
            sb.AppendLine("-----");
            sb.AppendLine($"Combination count: {generator.CombinationCount.ToString("N0", nfi)}");

            DebugData?.Invoke(sb.ToString());

            await Task.Yield();

            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                //MaxDegreeOfParallelism = 1, // to ease debugging
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            try
            {
                OrderablePartitioner<IEquipment[]> partitioner = Partitioner.Create(generator.All(cancellationToken), EnumerablePartitionerOptions.NoBuffering);

                ParallelLoopResult parallelResult = Parallel.ForEach(partitioner, parallelOptions, equips =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        searchEquipmentsObjectPool.PutObject(equips);
                        return;
                    }

                    ArmorSetSearchResult searchResult = IsArmorSetMatching(data.WeaponSlots, equips, data.AllJewels, desiredAbilities);

                    if (searchResult.IsMatch)
                    {
                        searchResult.ArmorPieces = equips.Take(5).Cast<IArmorPiece>().ToList();
                        searchResult.Charm = (ICharmLevel)equips[5];

                        lock (test)
                        {
                            test.Add(searchResult);
                        }
                    }

                    searchEquipmentsObjectPool.PutObject(equips);
                });
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            finally
            {
                generator.Reset();
            }

            sw.Stop();

            sb.AppendLine("-----");
            sb.AppendLine($"jewelResultObjectPool: {jewelResultObjectPool.Size}");
            sb.AppendLine($"availableSlotsObjectPool: {availableSlotsObjectPool.Size}");
            sb.AppendLine($"armorSetsObjectPool: {armorSetsObjectPool.Size}");
            sb.AppendLine($"searchEquipmentsObjectPool: {searchEquipmentsObjectPool.Size}");
            sb.AppendLine("-----");
            sb.AppendLine($"Matching result: {test.Count.ToString("N0", nfi)}");
            sb.AppendLine($"Took: {sw.ElapsedMilliseconds.ToString("N0", nfi)} ms");

            DebugData?.Invoke(sb.ToString());

            return test;
        }

        private ArmorSetSearchResult IsArmorSetMatching(
            int[] weaponSlots, IEquipment[] equipments,
            IList<SolverDataJewelModel> matchingJewels,
            IEnumerable<IAbility> desiredAbilities
        )
        {
            bool isOptimal = true;
            List<ArmorSetJewelResult> requiredJewels = jewelResultObjectPool.GetObject();
            int[] availableSlots = availableSlotsObjectPool.GetObject();

            void OnArmorSetMismatch()
            {
                requiredJewels.Clear();
                jewelResultObjectPool.PutObject(requiredJewels);

                for (int i = 0; i < availableSlots.Length; i++)
                    availableSlots[i] = 0;
                availableSlotsObjectPool.PutObject(availableSlots);
            }

            //if (
            //    equipments.Where(x => x != null).Any(x => x.Name == "Bazel Helm Beta") &&
            //    equipments.Where(x => x != null).Any(x => x.Name == "Kushala Cista Beta") &&
            //    equipments.Where(x => x != null).Any(x => x.Name == "High Metal Braces Beta") &&
            //    equipments.Where(x => x != null).Any(x => x.Name == "Bazel Coil Beta") &&
            //    equipments.Where(x => x != null).Any(x => x.Name == "Death Stench Heel Beta") &&
            //    equipments.Where(x => x != null).Any(x => x.Name == "Attack Charm 3")
            //)
            //{
            //}

            if (
                equipments.Where(x => x != null).Any(x => x.Name == "Strategist Spectacles α") &&
                equipments.Where(x => x != null).Any(x => x.Name == "Kulve Taroth's Ire α") &&
                equipments.Where(x => x != null).Any(x => x.Name == "Vaal Hazak Braces β") &&
                equipments.Where(x => x != null).Any(x => x.Name == "Odogaron Coil β") &&
                equipments.Where(x => x != null).Any(x => x.Name == "Dante's Leather Boots α") &&
                equipments.Where(x => x != null).Any(x => x.Name == "Master's Charm III")
            )
            {
            }

            if (weaponSlots != null)
            {
                foreach (int slotSize in weaponSlots)
                {
                    if (slotSize > 0)
                        availableSlots[slotSize - 1]++;
                }
            }

            foreach (IEquipment equipment in equipments)
            {
                if (equipment == null)
                    continue;

                foreach (int slotSize in equipment.Slots)
                    availableSlots[slotSize - 1]++;
            }

            foreach (IAbility ability in desiredAbilities)
            {
                int armorAbilityTotal = 0;

                if (IsAbilityMatchingArmorSet(ability, equipments.OfType<IArmorPiece>()))
                    continue;

                foreach (IEquipment equipment in equipments)
                {
                    if (equipment != null)
                    {
                        foreach (IAbility a in equipment.Abilities)
                        {
                            if (a.Skill.Id == ability.Skill.Id)
                                armorAbilityTotal += a.Level;
                        }
                    }
                }

                int remaingAbilityLevels = ability.Level - armorAbilityTotal;

                if (remaingAbilityLevels > 0)
                {
                    if (availableSlots.All(x => x <= 0))
                    {
                        OnArmorSetMismatch();
                        return new ArmorSetSearchResult { IsMatch = false };
                    }

                    foreach (SolverDataJewelModel j in matchingJewels)
                    {
                        // bold assumption, will be fucked if they decide to create jewels with multiple skills
                        IAbility a = j.Jewel.Abilities[0];

                        if (a.Skill.Id == ability.Skill.Id)
                        {
                            int requiredJewelCount = remaingAbilityLevels / a.Level;

                            if (j.Available < requiredJewelCount)
                            {
                                OnArmorSetMismatch();
                                return new ArmorSetSearchResult { IsMatch = false };
                            }

                            if (ConsumeSlots(availableSlots, j.Jewel.SlotSize, requiredJewelCount) == false)
                            {
                                OnArmorSetMismatch();
                                return new ArmorSetSearchResult { IsMatch = false };
                            }

                            remaingAbilityLevels -= requiredJewelCount * a.Level;

                            requiredJewels.Add(new ArmorSetJewelResult { Jewel = j.Jewel, Count = requiredJewelCount });

                            break;
                        }
                    }

                    if (remaingAbilityLevels > 0)
                    {
                        OnArmorSetMismatch();
                        return new ArmorSetSearchResult { IsMatch = false };
                    }
                }

                if (remaingAbilityLevels < 0)
                    isOptimal = false;
            }

            return new ArmorSetSearchResult
            {
                IsMatch = true,
                IsOptimal = isOptimal,
                Jewels = requiredJewels,
                SpareSlots = availableSlots
            };
        }

        private bool IsAbilityMatchingArmorSet(IAbility ability, IEnumerable<IArmorPiece> armorPieces)
        {
            Dictionary<IArmorSet, int> armorSets = armorSetsObjectPool.GetObject();

            void Done()
            {
                armorSets.Clear();
                armorSetsObjectPool.PutObject(armorSets);
            }

            foreach (IArmorPiece armorPiece in armorPieces)
            {
                if (armorPiece.ArmorSet == null || armorPiece.ArmorSet.IsFull)
                    continue;

                if (armorPiece.ArmorSet.Skills.SelectMany(x => x.GrantedSkills).Any(a => a.Id == ability.Id))
                {
                    if (armorSets.TryGetValue(armorPiece.ArmorSet, out int value) == false)
                        value = 0;

                    armorSets[armorPiece.ArmorSet] = value + 1;
                }
            }

            if (armorSets.Count > 0)
            {
                foreach (KeyValuePair<IArmorSet, int> armorSetKeyValue in armorSets)
                {
                    foreach (IArmorSetSkill armorSetSkill in armorSetKeyValue.Key.Skills)
                    {
                        if (armorSetKeyValue.Value >= armorSetSkill.RequiredArmorPieces)
                        {
                            if (armorSetSkill.GrantedSkills.Any(x => x.Id == ability.Id))
                            {
                                Done();
                                return true;
                            }
                        }
                    }
                }
            }

            Done();
            return false;
        }

        private bool ConsumeSlots(int[] availableSlots, int jewelSize, int jewelCount)
        {
            for (int i = jewelSize - 1; i < availableSlots.Length; i++)
            {
                if (availableSlots[i] <= 0)
                    continue;

                if (availableSlots[i] >= jewelCount)
                {
                    availableSlots[i] -= jewelCount;
                    return true;
                }
                else
                {
                    jewelCount -= availableSlots[i];
                    availableSlots[i] = 0;
                }
            }

            return jewelCount <= 0;
        }

        public class EquipmentCombinationGenerator
        {
            private readonly object syncRoot = new object();
            private readonly ObjectPool<IEquipment[]> searchEquipmentsObjectPool;
            private readonly IList<IEquipment>[] allEquipements;
            private readonly int[] indexes;
            private bool isEnd;

            public int CombinationCount { get; }

            public EquipmentCombinationGenerator(
                ObjectPool<IEquipment[]> searchEquipmentsObjectPool,
                IEnumerable<IEquipment> heads,
                IEnumerable<IEquipment> chests,
                IEnumerable<IEquipment> gloves,
                IEnumerable<IEquipment> waists,
                IEnumerable<IEquipment> legs,
                IEnumerable<IEquipment> charms
            )
            {
                this.searchEquipmentsObjectPool = searchEquipmentsObjectPool;

                allEquipements = new IList<IEquipment>[]
                {
                    heads.ToList(),
                    chests.ToList(),
                    gloves.ToList(),
                    waists.ToList(),
                    legs.ToList(),
                    charms.ToList()
                };

                indexes = new int[allEquipements.Length];

                int combinationCount = 1;
                for (int i = 0; i < allEquipements.Length; i++)
                    combinationCount *= allEquipements[i].Count;
                CombinationCount = combinationCount;
            }

            private bool Increment()
            {
                for (int i = 0; i < indexes.Length; i++)
                {
                    indexes[i]++;

                    if (indexes[i] < allEquipements[i].Count)
                        return true;

                    indexes[i] = 0;
                }

                return false;
            }

            public IEquipment[] Next(CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                    return null;

                lock (syncRoot)
                {
                    if (isEnd)
                        return null;

                    IEquipment[] equipments = searchEquipmentsObjectPool.GetObject();

                    for (int i = 0; i < equipments.Length; i++)
                        equipments[i] = allEquipements[i][indexes[i]];

                    isEnd = Increment() == false;

                    return equipments;
                }
            }

            public IEnumerable<IEquipment[]> All(CancellationToken cancellationToken)
            {
                IEquipment[] result;

                while ((result = Next(cancellationToken)) != null)
                    yield return result;
            }

            public void Reset()
            {
                for (int i = 0; i < indexes.Length; i++)
                    indexes[i] = 0;

                isEnd = false;
            }
        }

        private class ArmorSetEqualityComparer : IEqualityComparer<IArmorSet>
        {
            public static readonly IEqualityComparer<IArmorSet> Default = new ArmorSetEqualityComparer();

            public bool Equals(IArmorSet x, IArmorSet y)
            {
                if (x == null || y == null)
                    return false;

                return x.Id == y.Id;
            }

            public int GetHashCode(IArmorSet obj)
            {
                if (obj == null)
                    return 0;

                return obj.Id;
            }
        }
    }
}
