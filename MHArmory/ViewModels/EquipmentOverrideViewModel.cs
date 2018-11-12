using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using MHArmory.Configurations;
using MHArmory.Core.DataStructures;

namespace MHArmory.ViewModels
{
    public enum EquipmentOverrideVisibilityMode
    {
        /// <summary>
        /// Shows all sets.
        /// </summary>
        All,
        /// <summary>
        /// Show all sets where all armor pieces are owned.
        /// </summary>
        AllPossessed,
        /// <summary>
        /// Show only sets where at least one piece is owned.
        /// </summary>
        SomePossessed,
        /// <summary>
        /// Show only sets where at least one piece is not owned.
        /// </summary>
        SomeUnpossessed,
        /// <summary>
        /// Show only sets where no armor piece is owned.
        /// </summary>
        AllUnpossessed
    }

    public class EquipmentGroupViewModel : ViewModelBase
    {
        public string Name { get; }

        public IList<EquipmentViewModel> OrderedEquipments { get; }
        public IList<EquipmentViewModel> Equipments { get; }

        private bool isVisible = true;
        public bool IsVisible
        {
            get { return isVisible; }
            set { SetValue(ref isVisible, value); }
        }

        public bool PossessNone
        {
            get
            {
                return Equipments.All(x => x.IsPossessed == false);
            }
        }

        public bool PossessAll
        {
            get
            {
                return Equipments.All(x => x.IsPossessed);
            }
        }

        public bool PossessAny
        {
            get
            {
                return Equipments.Any(x => x.IsPossessed);
            }
        }

        public ICommand ToggleAllCommand { get; }

        private readonly EquipmentOverrideViewModel parent;

        public EquipmentGroupViewModel(EquipmentOverrideViewModel parent, IEnumerable<EquipmentViewModel> equipments)
        {
            this.parent = parent;

            Equipments = equipments.Where(x => x != null).ToList();

            if (Equipments.Count > 0)
            {
                if (Equipments[0].Equipment.Type != EquipmentType.Charm)
                {
                    OrderedEquipments = MakeArmorPieces(Equipments).ToList();
                    Name = FindGroupName(Equipments);
                }
                else
                {
                    OrderedEquipments = Equipments;
                    Name = ((ICharmLevel)Equipments[0].Equipment).Charm.Name;
                }
            }

            ToggleAllCommand = new AnonymousCommand(OnToggleAll);
        }

        private static IEnumerable<EquipmentViewModel> MakeArmorPieces(IEnumerable<EquipmentViewModel> equipments)
        {
            yield return equipments.FirstOrDefault(x => x.Type == EquipmentType.Head);
            yield return equipments.FirstOrDefault(x => x.Type == EquipmentType.Chest);
            yield return equipments.FirstOrDefault(x => x.Type == EquipmentType.Gloves);
            yield return equipments.FirstOrDefault(x => x.Type == EquipmentType.Waist);
            yield return equipments.FirstOrDefault(x => x.Type == EquipmentType.Legs);
        }

        public void ApplySearchText(SearchStatement searchStatement)
        {
            if (searchStatement == null || searchStatement.IsEmpty)
            {
                IsVisible = true;
                return;
            }

            IsVisible =
                searchStatement.IsMatching(Name) ||
                Equipments.Any(x => searchStatement.IsMatching(x.Name));
        }

        private void OnToggleAll()
        {
            bool allChecked = Equipments.All(x => x.IsPossessed);

            foreach (EquipmentViewModel equipment in Equipments)
                equipment.IsPossessed = allChecked == false;
        }

        public static string FindGroupName(IEnumerable<IEquipment> equipments)
        {
            if (equipments == null)
                return null;

            string baseName = null;
            int firstPartMinLength = 0;
            int lastPartMinLength = 0;

            foreach (IEquipment eqp in equipments)
            {
                if (eqp == null)
                    continue;

                if (baseName == null)
                {
                    baseName = eqp.Name;
                    firstPartMinLength = baseName.Length;
                    lastPartMinLength = baseName.Length;
                    continue;
                }

                int c;
                string name = eqp.Name;

                for (c = 0; c < name.Length && c < baseName.Length; c++)
                {
                    if (name[c] != baseName[c])
                        break;
                }

                if (c < firstPartMinLength)
                    firstPartMinLength = c;

                for (c = 0; c < name.Length && c < baseName.Length; c++)
                {
                    if (name[name.Length - c - 1] != baseName[baseName.Length - c - 1])
                        break;
                }

                if (c < lastPartMinLength)
                    lastPartMinLength = c;
            }

            if (firstPartMinLength == 0)
                return null;

            if (lastPartMinLength == 0 || firstPartMinLength == lastPartMinLength)
            {
                if (firstPartMinLength == baseName.Length)
                    return baseName;

                return baseName.Substring(0, firstPartMinLength).Trim();
            }

            string firstPart = baseName.Substring(0, firstPartMinLength).Trim();
            string lastPart = baseName.Substring(baseName.Length - lastPartMinLength).Trim();

            return $"{firstPart} {lastPart}";
        }
    }

    public class EquipmentOverrideViewModel : ViewModelBase
    {
        private readonly RootViewModel rootViewModel;

        private IList<EquipmentGroupViewModel> armorSets;
        public IList<EquipmentGroupViewModel> ArmorSets
        {
            get { return armorSets; }
            private set { SetValue(ref armorSets, value); }
        }

        private string searchText;
        public string SearchText
        {
            get { return searchText; }
            set
            {
                if (SetValue(ref searchText, value))
                    ComputeVisibility();
            }
        }

        private string status;
        public string Status
        {
            get { return status; }
            private set { SetValue(ref status, value); }
        }

        private EquipmentOverrideVisibilityMode visibilityMode = EquipmentOverrideVisibilityMode.All;
        public EquipmentOverrideVisibilityMode VisibilityMode
        {
            get { return visibilityMode; }
            set
            {
                if (SetValue(ref visibilityMode, value))
                    ComputeVisibility();
            }
        }

        public ICommand CancelCommand { get; }

        public EquipmentOverrideViewModel(RootViewModel rootViewModel)
        {
            this.rootViewModel = rootViewModel;

            CancelCommand = new AnonymousCommand(OnCancel);
        }

        private void OnCancel(object parameter)
        {
            if (parameter is CancellationCommandArgument cancellable)
            {
                if (string.IsNullOrWhiteSpace(SearchText) == false)
                {
                    SearchText = string.Empty;
                    cancellable.IsCancelled = true;
                }
            }
        }

        public void ComputeVisibility()
        {
            var searchStatement = SearchStatement.Create(SearchText);

            foreach (EquipmentGroupViewModel vm in ArmorSets)
                ComputeVisibility(vm, searchStatement);

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            Status = $"{ArmorSets.Count(x => x.IsVisible)} sets";
        }

        private void ComputeVisibility(EquipmentGroupViewModel group, SearchStatement searchStatement)
        {
            if (visibilityMode == EquipmentOverrideVisibilityMode.AllPossessed)
            {
                if (group.PossessAll == false)
                {
                    group.IsVisible = false;
                    return;
                }
            }
            else if (visibilityMode == EquipmentOverrideVisibilityMode.SomePossessed)
            {
                if (group.PossessAny == false)
                {
                    group.IsVisible = false;
                    return;
                }
            }
            else if (visibilityMode == EquipmentOverrideVisibilityMode.SomeUnpossessed)
            {
                if (group.PossessAll || group.PossessNone)
                {
                    group.IsVisible = false;
                    return;
                }
            }
            else if (visibilityMode == EquipmentOverrideVisibilityMode.AllUnpossessed)
            {
                if (group.PossessAny)
                {
                    group.IsVisible = false;
                    return;
                }
            }

            if (searchStatement == null)
                searchStatement = SearchStatement.Create(searchText);

            group.ApplySearchText(searchStatement);
        }

        internal void NotifyDataLoaded()
        {
            ArmorSets = rootViewModel.AllEquipments
                .Where(x => x.Type != EquipmentType.Weapon)
                .GroupBy(GroupOperator)
                .Select(x => new EquipmentGroupViewModel(this, x))
                .OrderBy(x => x.Name)
                .ToList();

            LoadConfiguration();

            UpdateStatus();
        }

        private static int GroupOperator(EquipmentViewModel eqp)
        {
            if (eqp.Type != EquipmentType.Charm)
                return eqp.Id;

            return ((ICharmLevel)eqp.Equipment).Charm.Id + 10000;
        }

        private EquipmentViewModel FindEquipmentByName(string name)
        {
            foreach (EquipmentGroupViewModel group in ArmorSets)
            {
                foreach (EquipmentViewModel equipment in group.Equipments)
                {
                    if (equipment.Name == name)
                        return equipment;
                }
            }

            return null;
        }

        private void LoadConfiguration()
        {
            EquipmentOverrideConfigurationV2 configuration = GlobalData.Instance.Configuration.InParameters.EquipmentOverride;

            if (configuration.IsStoringPossessed)
            {
                foreach (EquipmentGroupViewModel group in ArmorSets)
                {
                    foreach (EquipmentViewModel equipment in group.Equipments)
                        equipment.IsPossessed = configuration.Items.Contains(equipment.Name);
                }
            }
            else
            {
                foreach (EquipmentGroupViewModel group in ArmorSets)
                {
                    foreach (EquipmentViewModel equipment in group.Equipments)
                        equipment.IsPossessed = configuration.Items.Contains(equipment.Name) == false;
                }
            }
        }

        public void SaveConfiguration()
        {
            int total = 0;
            int totalPossessed = 0;

            foreach (EquipmentGroupViewModel group in ArmorSets)
            {
                foreach (EquipmentViewModel equipment in group.Equipments)
                {
                    total++;
                    if (equipment.IsPossessed)
                        totalPossessed++;
                }
            }

            EquipmentOverrideConfigurationV2 configuration = GlobalData.Instance.Configuration.InParameters.EquipmentOverride;

            configuration.UseOverride = rootViewModel.InParameters.UseEquipmentOverride;
            configuration.Items.Clear();

            if (totalPossessed < (total - totalPossessed))
            {
                // save possessed ones
                configuration.IsStoringPossessed = true;

                foreach (EquipmentGroupViewModel group in ArmorSets)
                {
                    foreach (EquipmentViewModel equipment in group.Equipments.Where(x => x.IsPossessed))
                        configuration.Items.Add(equipment.Name);
                }
            }
            else
            {
                // save not possessed ones
                configuration.IsStoringPossessed = false;

                foreach (EquipmentGroupViewModel group in ArmorSets)
                {
                    foreach (EquipmentViewModel equipment in group.Equipments.Where(x => x.IsPossessed == false))
                        configuration.Items.Add(equipment.Name);
                }
            }

            ConfigurationManager.Save(GlobalData.Instance.Configuration);
        }
    }
}
