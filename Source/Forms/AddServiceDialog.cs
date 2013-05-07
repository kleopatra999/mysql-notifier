﻿//
// Copyright (c) 2012-2013, Oracle and/or its affiliates. All rights reserved.
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License as
// published by the Free Software Foundation; version 2 of the
// License.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA
// 02110-1301  USA
//

namespace MySql.Notifier
{
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using System.Management;
  using System.Windows.Forms;
  using MySql.Notifier.Properties;
  using MySQL.Utility;
  using MySQL.Utility.Forms;

  public partial class AddServiceDialog : MachineAwareForm
  {
    private int sortColumn;
    private bool machineValuesChanged;

    public AddServiceDialog(MachinesList machineslist, Machine machine)
    {
      sortColumn = -1;
      machineValuesChanged = false;

      InitializeComponent();
      MachineSelectionComboBox.SelectedIndex = 0;
      ServicesListView.ColumnClick += new ColumnClickEventHandler(ServicesListView_ColumnClick);
      newMachine = machine ?? newMachine;
      machinesList = machineslist;
      InsertMachinesIntoComboBox();
    }

    public Machine.LocationType MachineLocationType { get; set; }

    public List<MySQLService> ServicesToAdd { get; set; }

    private void DialogOKButton_Click(object sender, EventArgs e)
    {
      Cursor.Current = Cursors.WaitCursor;
      ServicesToAdd = new List<MySQLService>();
      foreach (ListViewItem lvi in ServicesListView.SelectedItems)
      {
        ServicesToAdd.Add(new MySQLService(lvi.Tag as string, true, true, newMachine));
      }

      if (machineValuesChanged)
      {
        Settings.Default.Save();
        machineValuesChanged = false;
      }

      Cursor.Current = Cursors.Default;
    }

    private void FilterCheckBox_CheckedChanged(object sender, EventArgs e)
    {
      RefreshList();
    }

    private void FilterTextBox_TextChanged(object sender, EventArgs e)
    {
      if (!timerForFiltering.Enabled)
      {
        timerForFiltering.Enabled = true;
      }
    }

    private void InsertMachinesIntoComboBox()
    {
      if (machinesList == null) return;
      if (machinesList.Machines == null) return;

      foreach (Machine machine in machinesList.Machines)
      {
        if (machine.Name != "localhost")
        {
          MachineSelectionComboBox.Items.Add(machine);
        }
      }
    }

    private void MachineAutoTestConnectionIntervalNumericUpDown_ValueChanged(object sender, EventArgs e)
    {
      if (newMachine != null)
      {
        newMachine.AutoTestConnectionInterval = (uint)MachineAutoTestConnectionIntervalNumericUpDown.Value;
        machineValuesChanged = true;
      }
    }

    private void MachineAutoTestConnectionIntervalUOMComboBox_SelectedIndexChanged(object sender, EventArgs e)
    {
      if (newMachine != null)
      {
        newMachine.AutoTestConnectionIntervalUnitOfMeasure = (TimeUtilities.IntervalUnitOfMeasure)MachineAutoTestConnectionIntervalUOMComboBox.SelectedIndex;
        machineValuesChanged = true;
      }
    }

    private void MachineSelectionComboBox_SelectedIndexChanged(object sender, EventArgs e)
    {
      DialogResult dr = DialogResult.None;
      switch (MachineSelectionComboBox.SelectedIndex)
      {
        case 0:
          MachineLocationType = Machine.LocationType.Local;
          if (newMachine != null && newMachine.IsLocal)
          {
            break;
          }

          newMachine = new Machine();
          break;

        case 1:
          MachineLocationType = Machine.LocationType.Remote;
          using (var windowsConnectionDialog = new WindowsConnectionDialog(machinesList, newMachine))
          {
            dr = windowsConnectionDialog.ShowDialog();
            if (dr == DialogResult.Cancel)
            {
              MachineSelectionComboBox.SelectedIndex = 0;
            }
            else
            {
              newMachine = windowsConnectionDialog.newMachine;
              newMachine.LoadServicesParameters();
              int index = -1;
              for (int machineIndex = 3; machineIndex < MachineSelectionComboBox.Items.Count && index < 0; machineIndex++)
              {
                string machineName = MachineSelectionComboBox.Items[machineIndex].ToString();
                if (machineName == newMachine.Name)
                {
                  index = machineIndex;
                }
              }

              if (index == -1)
              {
                MachineSelectionComboBox.Items.Add(newMachine);
                MachineSelectionComboBox.SelectedIndex = MachineSelectionComboBox.Items.Count - 1;
              }
              else
              {
                MachineSelectionComboBox.SelectedIndex = index <= 0 ? 0 : index;
              }
            }
          }
          return;

        case 2:
          if (newMachine.IsLocal)
          {
            MachineSelectionComboBox.SelectedIndex = 0;
            return;
          }

          int mIndex = -1;
          for (int machineIndex = 3; machineIndex < MachineSelectionComboBox.Items.Count; machineIndex++)
          {
            string machineName = MachineSelectionComboBox.Items[machineIndex].ToString();
            if (machineName == newMachine.Name)
            {
              mIndex = machineIndex;
              break;
            }
          }

          MachineSelectionComboBox.SelectedIndex = mIndex < 0 ? 0 : mIndex;
          return;

        default:
          Cursor.Current = Cursors.WaitCursor;
          MachineLocationType = Machine.LocationType.Remote;
          newMachine = (Machine)MachineSelectionComboBox.SelectedItem;
          if (!newMachine.IsOnline)
          {
            InfoDialog.ShowInformationDialog(Resources.HostUnavailableTitle, Resources.HostUnavailableMessage);
            ServicesListView.SelectedItems.Clear();
          }

          Cursor.Current = Cursors.Default;
          break;
      }

      SetMachineAutoTestConnectionControlsAvailability();
      ServicesListView.Enabled = newMachine.IsOnline;
      Machine servicesMachine = ServicesListView.Tag as Machine;
      if (servicesMachine != newMachine)
      {
        RefreshList();
      }
    }

    private void RefreshList()
    {
      //// Store the machine used to browse services so we can compare it with the current value in newMachine to know if we need to call RefreshList.
      ServicesListView.Tag = newMachine;

      ServicesListView.Items.Clear();
      if (newMachine == null || !newMachine.IsOnline)
      {
        return;
      }

      string currentFilter = FilterCheckBox.Checked ? Settings.Default.AutoAddPattern.Trim() : FilterTextBox.Text.ToLower();

      ServicesListView.BeginUpdate();
      List<ManagementObject> services = new List<ManagementObject>();
      if (newMachine != null && MachineLocationType == Machine.LocationType.Remote)
      {
        ManagementObjectCollection machineServicesCollection = newMachine.GetWMIServices(true);
        if (machineServicesCollection != null)
        {
          foreach (ManagementObject mo in machineServicesCollection)
          {
            services.Add(mo);
          }
        }
      }
      else
      {
        services = Service.GetInstances(String.Empty);
      }

      services = services.OrderBy(x => x.Properties["DisplayName"].Value).ToList();
      if (!string.IsNullOrEmpty(currentFilter))
      {
        services = services.Where(f => f.Properties["DisplayName"].Value.ToString().ToLower().Contains(currentFilter)).ToList();
      }

      foreach (ManagementObject item in services)
      {
        ListViewItem newItem = new ListViewItem();
        newItem.Text = item.Properties["DisplayName"].Value.ToString();
        newItem.Tag = item.Properties["Name"].Value.ToString();
        newItem.SubItems.Add(item.Properties["State"].Value.ToString());
        ServicesListView.Items.Add(newItem);
      }

      ServicesListView.EndUpdate();
    }

    private void ServicesListView_ColumnClick(object sender, ColumnClickEventArgs e)
    {
      if (e.Column != sortColumn)
      {
        sortColumn = e.Column;
        ServicesListView.Sorting = SortOrder.Ascending;
      }
      else
      {
        if (ServicesListView.Sorting == SortOrder.Ascending)
          ServicesListView.Sorting = SortOrder.Descending;
        else
          ServicesListView.Sorting = SortOrder.Ascending;
      }

      ServicesListView.Sort();
      ServicesListView.ListViewItemSorter = new ListViewItemComparer(e.Column, ServicesListView.Sorting);
    }

    private void SetMachineAutoTestConnectionControlsAvailability()
    {
      MachineAutoTestConnectionIntervalNumericUpDown.Value = MachineSelectionComboBox.SelectedIndex > 2 ? newMachine.AutoTestConnectionInterval : 0;
      MachineAutoTestConnectionIntervalNumericUpDown.Enabled = MachineSelectionComboBox.SelectedIndex > 2;
      MachineAutoTestConnectionIntervalUOMComboBox.Enabled = MachineSelectionComboBox.SelectedIndex > 2;
      if (MachineSelectionComboBox.SelectedIndex > 2)
      {
        MachineAutoTestConnectionIntervalUOMComboBox.SelectedIndex = (int)newMachine.AutoTestConnectionIntervalUnitOfMeasure;
      }
      else
      {
        MachineAutoTestConnectionIntervalUOMComboBox.Text = string.Empty;
      }
    }

    private void timerForFiltering_Tick(object sender, EventArgs e)
    {
      RefreshList();
      timerForFiltering.Enabled = false;
    }

    private class ListViewItemComparer : IComparer
    {
      private int col;
      private SortOrder order;

      public ListViewItemComparer()
      {
        col = 0;
        order = SortOrder.Ascending;
      }

      public ListViewItemComparer(int column, SortOrder order)
      {
        col = column;
        this.order = order;
      }

      public int Compare(object x, object y)
      {
        int returnVal = -1;

        returnVal = String.Compare(((ListViewItem)x).SubItems[col].Text, ((ListViewItem)y).SubItems[col].Text);

        if (order == SortOrder.Descending)
          returnVal *= -1;

        return returnVal;
      }
    }
  }
}