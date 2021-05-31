using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms;

namespace PocketPCRController
{ 
    /// <summary>
    /// Data model and GUI elements for editing a themalcycling program
    /// </summary>
    public class CyclingEditModel
    {
        /// <summary>
        /// Width of cycling step to display on the GUI
        /// <para>
        /// currently temperature-time steps are displayed with a constant width
        /// </para>
        /// </summary>
        public double BlockWidth { get; set; } 
        /// <summary>
        /// A set width between temperature lines to display a diagonal transition line between temperature settings
        /// </summary>
        public double TransitionWidth { get; set; }
        /// <summary>
        ///  Grid in which to display GUI elements
        /// </summary>
        public Grid ParentPanel { get; set; }
        /// <summary>
        /// Index in the Programs list corresponding to the current cycling program
        /// </summary>
        public int ProgramIndex { get; set; }
        /// <summary>
        /// Parent Window displaying the CyclingEditModel
        /// </summary>
        public CyclingEditWindow ParentWindow { get; set; }
        /// <summary>
        /// Current cycling program to display for editing
        /// </summary>
        public PCRProgram Program { get; set; }
        /// <summary>
        /// List of EditCycle objects, each defining a series of themalcycling temperature/time steps
        /// </summary>
        public List<EditCycle> EditCycles { get; set; }
        /// <summary>
        /// Constructor for a CyclingEditModel object
        /// </summary>
        /// <param name="_parentWindow">Parent Window to display the CyclingEditModel</param>
        /// <param name="_programIndex">Index in the Programs list corresponding to the current cycling program</param>
        /// <param name="_parentPanel"> Grid in which to display GUI elements for the thermalcycling progam</param>
        /// <param name="_program">Current cycling program to display for editing</param>
        public CyclingEditModel(CyclingEditWindow _parentWindow, int _programIndex, Grid _parentPanel, PCRProgram _program)
        {
            ParentWindow = _parentWindow;
            ProgramIndex = _programIndex;
            BlockWidth = 100;
            TransitionWidth = 20;
            ParentPanel = _parentPanel;
            Program = _program;
            EditCycles = new List<EditCycle>();
            ParentPanel.ColumnDefinitions.Clear();
            ParentPanel.Width = 0;
            for (int i = 0; i < Program.Cycles.Count; i++)
            {
                EditCycle newCycle = new EditCycle(this, Program.Cycles[i], i);
                EditCycles.Add(newCycle);
            }
        }
    }

    /// <summary>
    /// Class to display and allow editing of a PCRCycle object (an open-ended series of themalcycling temperature/time steps that can be repeated an arbitrary number of times)
    /// </summary>
    public class EditCycle
    {
        /// <summary>
        /// CyclingEditModel that this EditCycle object belongs to
        /// </summary>
        public CyclingEditModel ParentModel { get; set; }
        /// <summary>
        /// Index in ParentModel.EditCycles List that this EditCycle object corresponds to
        /// </summary>
        public int CycleIndex { get; set; }
        /// <summary>
        /// The PCRCycle object in the PCRProgram that this EditCycle object corresponds to
        /// </summary>
        public PCRCycle Cycle { get; set; }
        /// <summary>
        /// List of temperature/time steps
        /// </summary>
        public List<EditBlock> EditBlocks { get; set; }
        /// <summary>
        /// A button to increase the number of EditBlock by one
        /// </summary>
        public System.Windows.Controls.Button InsertBlocksButton { get; set; }
        /// <summary>
        /// A button to decrease the number of EditBlock by one
        /// </summary>
        public System.Windows.Controls.Button DeleteBlocksButton { get; set; }
        // <summary>
        /// A button to allow deleting this EditCycle
        /// </summary>
        public System.Windows.Controls.Button DeleteStepButton { get; set; }
        // <summary>
        /// A button to allow inserting an EditCycle before this one
        /// </summary>
        public System.Windows.Controls.Button InsertStepButton { get; set; }
        /// <summary>
        /// The Grid on which to display this EditCycle
        /// </summary>
        public Grid CycleGrid { get; set; }
        /// <summary>
        /// A label to display the current number of temperrature/time steps in the Cycle
        /// </summary>
        public System.Windows.Controls.Label BlocksLabel { get; set; }
        /// <summary>
        /// Label to simply display the word "Cycles" in front of the TextBox specifying the number of times to repeat this Cycle
        /// </summary>
        public System.Windows.Controls.Label CyclesLabel { get; set; }
        /// <summary>
        /// TextBox specifying the number of times to repeat this Cycle
        /// </summary>
        public System.Windows.Controls.TextBox CyclesBox { get; set; }
        /// <summary>
        /// Boolean flag specifying if this object has been removed to avoid displaying it prior to garbage collection
        /// </summary>
        public bool Removed { get; set; }
        /// <summary>
        /// Constructor for an EditCycle object
        /// </summary>
        /// <param name="_parentModel">CyclingEditModel that this EditCycle object belongs to</param>
        /// <param name="_cycle">The PCRCycle object in the PCRProgram that this EditCycle object corresponds to</param>
        /// <param name="_cycleIndex">Index in ParentModel.EditCycles List that this EditCycle object corresponds to</param>
        public EditCycle(CyclingEditModel _parentModel, PCRCycle _cycle, int _cycleIndex)
        {
            Removed = false;
            ParentModel = _parentModel;
            CycleIndex = _cycleIndex;
            Cycle = _cycle;

            // Setup GUI elements
            CycleGrid = new Grid();
            CycleGrid.Width = ParentModel.BlockWidth * Convert.ToDouble(Cycle.Blocks.Count);           
            ParentModel.ParentPanel.ColumnDefinitions.Add(new ColumnDefinition());
            ParentModel.ParentPanel.ColumnDefinitions[ParentModel.ParentPanel.ColumnDefinitions.Count - 1].Width = new System.Windows.GridLength(CycleGrid.Width);
            ParentModel.ParentPanel.Width += CycleGrid.Width;
            Border CycleBorder = new Border();
            CycleBorder.BorderThickness = new System.Windows.Thickness(5);
            CycleBorder.BorderBrush = System.Windows.Media.Brushes.Green;
            Grid.SetColumn(CycleBorder, ParentModel.ParentPanel.ColumnDefinitions.Count - 1);
            ParentModel.ParentPanel.Children.Add(CycleBorder);
            Grid.SetColumn(CycleGrid, ParentModel.ParentPanel.ColumnDefinitions.Count - 1);
            ParentModel.ParentPanel.Children.Add(CycleGrid);
            EditBlocks = new List<EditBlock>();
            for (int i = 0; i < Cycle.Blocks.Count; i++)
            {
                EditBlock newBlock = new EditBlock(this, Cycle.Blocks[i]);
                EditBlocks.Add(newBlock);
            }
            InsertBlocksButton = new System.Windows.Controls.Button();
            InsertBlocksButton.Tag = this;
            InsertBlocksButton.Content = "+";
            InsertBlocksButton.ToolTip = "Add a step in this cycle";
            InsertBlocksButton.Width = 14;
            InsertBlocksButton.Height = 22;
            InsertBlocksButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            InsertBlocksButton.VerticalAlignment = System.Windows.VerticalAlignment.Top;
            InsertBlocksButton.Margin = new System.Windows.Thickness(5, 5, 0, 0);
            InsertBlocksButton.Click += InsertBlocksButton_Click;
            Grid.SetColumn(InsertBlocksButton, 0);
            CycleGrid.Children.Add(InsertBlocksButton);
            DeleteBlocksButton = new System.Windows.Controls.Button();
            DeleteBlocksButton.Tag = this;
            DeleteBlocksButton.Content = "-";
            DeleteBlocksButton.ToolTip = "Remove a step from this cycle";
            DeleteBlocksButton.Width = 14;
            DeleteBlocksButton.Height = 22;
            DeleteBlocksButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            DeleteBlocksButton.VerticalAlignment = System.Windows.VerticalAlignment.Top;
            DeleteBlocksButton.Margin = new System.Windows.Thickness(19, 5, 0, 0);
            DeleteBlocksButton.Click += DeleteBlocksButton_Click;
            if (Cycle.Blocks.Count <= 1)
            {
                DeleteBlocksButton.IsEnabled = false;
            }
            Grid.SetColumn(DeleteBlocksButton, 0);
            CycleGrid.Children.Add(DeleteBlocksButton);
            BlocksLabel = new System.Windows.Controls.Label();
            BlocksLabel.Height = 26;
            BlocksLabel.Content = Cycle.Blocks.Count.ToString();
            BlocksLabel.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            BlocksLabel.VerticalAlignment = System.Windows.VerticalAlignment.Top;
            BlocksLabel.Margin = new System.Windows.Thickness(35, 3, 0, 0);
            Grid.SetColumn(BlocksLabel, 0);
            CycleGrid.Children.Add(BlocksLabel);
            DeleteStepButton = new System.Windows.Controls.Button();
            DeleteStepButton.Tag = this;
            DeleteStepButton.Content = "X";
            DeleteStepButton.ToolTip = "Delete this cycle";
            DeleteStepButton.Width = 14;
            DeleteStepButton.Height = 22;
            DeleteStepButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            DeleteStepButton.VerticalAlignment = System.Windows.VerticalAlignment.Top;
            DeleteStepButton.Margin = new System.Windows.Thickness(0, 5, 5, 0);
            DeleteStepButton.Click += DeleteStepButton_Click;
            Grid.SetColumn(DeleteStepButton, CycleGrid.ColumnDefinitions.Count - 1);
            CycleGrid.Children.Add(DeleteStepButton);
            InsertStepButton = new System.Windows.Controls.Button();
            InsertStepButton.Tag = this;
            InsertStepButton.Content = "+";
            InsertStepButton.Width = 14;
            InsertStepButton.Height = 22;
            InsertStepButton.Click += InsertStepButton_Click;
            InsertStepButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            InsertStepButton.VerticalAlignment = System.Windows.VerticalAlignment.Top;
            InsertStepButton.Margin = new System.Windows.Thickness(0, 5, 19, 0);
            InsertStepButton.ToolTip = "Insert a cycle before this cycle";
            Grid.SetColumn(InsertStepButton, CycleGrid.ColumnDefinitions.Count - 1);
            CycleGrid.Children.Add(InsertStepButton);
            CyclesBox = new System.Windows.Controls.TextBox();
            CyclesBox.Width = 45;
            CyclesBox.Height = 22;
            CyclesBox.Margin = new System.Windows.Thickness(45, 0, 0, 5);
            CyclesBox.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            CyclesBox.VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
            CyclesBox.Text = Cycle.NumberOfCycles.ToString();
            CyclesBox.LostFocus += CyclesBox_LostFocus;
            CyclesBox.KeyUp += CyclesBox_KeyUp;
            CyclesBox.ToolTip = "Change the number of times this cycle repeats";
            Grid.SetColumn(CyclesBox, CycleGrid.ColumnDefinitions.Count - 1);
            CycleGrid.Children.Add(CyclesBox);
            CyclesLabel = new System.Windows.Controls.Label();
            CyclesLabel.Content = "Cycles";
            CyclesLabel.Height = 26;
            CyclesLabel.Margin = new System.Windows.Thickness(2, 0, 0, 3);
            CyclesLabel.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            CyclesLabel.VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
            Grid.SetColumn(CyclesLabel, CycleGrid.ColumnDefinitions.Count - 1);
            CycleGrid.Children.Add(CyclesLabel);
        }

        /// <summary>
        /// The button to decrease the number of steps in this cycling block was pressed
        /// <para>
        /// Remove the last step in the cycle
        /// </para>
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DeleteBlocksButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (Cycle.Blocks.Count > 1)
            {
                Cycle.Blocks.RemoveAt(Cycle.Blocks.Count - 1);
                if (Cycle.Blocks.Count == 1)
                {
                    DeleteBlocksButton.IsEnabled = false;
                }
                ParentModel.ParentWindow.UpdateEditInterface(ParentModel.ProgramIndex);
            }
            else
            {
                System.Windows.Forms.MessageBox.Show("Error: Cannot remove the only step.\nRemove the entire cycle instead.");
            }
        }

        /// <summary>
        /// The button to increase the number of steps in this cycling block was pressed
        /// <para>
        /// Replicate the last step in the cycle and allow it to be edited. 
        /// If there are no steps in the cycle, add a default of 50 degrees Celcius for 20 seconds 
        /// (this is currently an impossible situation, but the logic is a placeholder for a 
        /// condition that could arise through a code change).
        /// </para>
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void InsertBlocksButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (Cycle.Blocks.Count > 0)
            {
                Cycle.Add(Cycle.Blocks[Cycle.Blocks.Count - 1].TargetTemperature, Cycle.Blocks[Cycle.Blocks.Count - 1].TargetTimeSeconds);
            }
            else
            {
                Cycle.Add(50, 20);
            }
            ParentModel.ParentWindow.UpdateEditInterface(ParentModel.ProgramIndex);
        }

        /// <summary>
        /// Update the program if the Enter key is pressed after editing the contents of CyclesBox
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CyclesBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                updateCyclesBox();
            }
        }

        /// <summary>
        /// Update the program according to a modified value in CyclesBox
        /// </summary>
        public void updateCyclesBox()
        {
            try
            {
                int newVal = Convert.ToInt32(CyclesBox.Text);
                if (newVal <= 0 && !Removed)
                {
                    DialogResult dialogResult = System.Windows.Forms.MessageBox.Show("Remove cycle?", "Cycle Removal Confirmation", MessageBoxButtons.YesNo);
                    if (dialogResult != System.Windows.Forms.DialogResult.Yes)
                    {
                        CyclesBox.Text = "1";
                        return;
                    }
                    Removed = true;
                    ParentModel.Program.Cycles.RemoveAt(CycleIndex);
                    ParentModel.Program.RecountCycles();
                    ParentModel.ParentWindow.UpdateEditInterface(ParentModel.ProgramIndex);
                }
                else
                {
                    if (newVal != Cycle.NumberOfCycles)
                    {
                        Cycle.NumberOfCycles = newVal;
                        ParentModel.Program.RecountCycles();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Error: Unable to convert " + CyclesBox.Text + " to a number of cycles.");
            }
        }

        /// <summary>
        /// Update the program if focus is removed from CyclesBox after editing the contents of CyclesBox
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CyclesBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            updateCyclesBox();
        }

        /// <summary>
        /// User pressed the button specifying deletion of this Cycle
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DeleteStepButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (!Removed)
            {
                DialogResult dialogResult = System.Windows.Forms.MessageBox.Show("Remove cycle?", "Cycle Removal Confirmation", MessageBoxButtons.YesNo);
                if (dialogResult != System.Windows.Forms.DialogResult.Yes) return;
                Removed = true;
                ParentModel.Program.Cycles.RemoveAt(CycleIndex);
                ParentModel.Program.RecountCycles();
                ParentModel.ParentWindow.UpdateEditInterface(ParentModel.ProgramIndex);
            }
        }

        /// <summary>
        /// User pressed the button specifying insertion of a Cycle before this Cycle
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void InsertStepButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            DialogResult dialogResult = System.Windows.Forms.MessageBox.Show("Inert a cycle before this one?", "Cycle Removal Confirmation", MessageBoxButtons.YesNo);
            if (dialogResult != System.Windows.Forms.DialogResult.Yes) return;
            PCRCycle newCycle = new PCRCycle();
            newCycle.Add(95, 15);
            newCycle.Add(56, 15);
            newCycle.Add(72, 15);
            newCycle.NumberOfCycles = 1;
            ParentModel.Program.Cycles.Insert(CycleIndex, newCycle);
            ParentModel.Program.RecountCycles();
            ParentModel.ParentWindow.UpdateEditInterface(ParentModel.ProgramIndex);
        }
    }

    /// <summary>
    /// Class to display a temperature/time step in a thermalcycling program
    /// </summary>
    public class EditBlock
    {
        /// <summary>
        /// Parent EditCycle GUI element within which this temperature/time step is displayed
        /// </summary>
        public EditCycle ParentCycle { get; set; }
        /// <summary>
        /// Grid column in which this temperature/time step is displayed
        /// </summary>
        public int CycleColumn { get; set; }
        /// <summary>
        /// PCRBlock object this element corresponds to
        /// </summary>
        public PCRBlock Block { get; set; }
        /// <summary>
        /// Drawing canvas to display this EditBlock object
        /// </summary>
        public Canvas BlockCanvas { get; set; }
        /// <summary>
        /// TextBox to display and allow modification of the temperature target for the corresponding PCRBlock
        /// </summary>
        public System.Windows.Controls.TextBox TempSettingBox { get; set; }
        /// <summary>
        /// TextBox to display and allow modification of the time (in seconds) target for the corresponding PCRBlock
        /// </summary>
        public System.Windows.Controls.TextBox TimeSettingBox { get; set; }
        /// <summary>
        /// Label to display "Temp"
        /// </summary>
        public System.Windows.Controls.Label TempSettingLabel { get; set; }
        /// <summary>
        /// Label to display "Time"
        /// </summary>
        public System.Windows.Controls.Label TimeSettingLabel { get; set; }
        /// <summary>
        /// Constructor for an EditBlock object
        /// </summary>
        /// <param name="_parentCycle">Parent EditCycle GUI element within which this temperature/time step is displayed</param>
        /// <param name="_block">PCRBlock object this element corresponds to</param>
        public EditBlock(EditCycle _parentCycle, PCRBlock _block)
        {
            ParentCycle = _parentCycle;
            Block = _block;
            BlockCanvas = new Canvas();
            ParentCycle.CycleGrid.ColumnDefinitions.Add(new ColumnDefinition());
            ParentCycle.CycleGrid.ColumnDefinitions[ParentCycle.CycleGrid.ColumnDefinitions.Count - 1].Width = new System.Windows.GridLength(ParentCycle.ParentModel.BlockWidth);

            Border BlockBorder = new Border();
            BlockBorder.BorderThickness = new System.Windows.Thickness(1);
            BlockBorder.BorderBrush = System.Windows.Media.Brushes.Black;
            Grid.SetColumn(BlockBorder, ParentCycle.CycleGrid.ColumnDefinitions.Count - 1);
            ParentCycle.CycleGrid.Children.Add(BlockBorder);

            Grid.SetColumn(BlockCanvas, ParentCycle.CycleGrid.ColumnDefinitions.Count - 1);
            ParentCycle.CycleGrid.Children.Add(BlockCanvas);
            TempSettingBox = new System.Windows.Controls.TextBox();
            TimeSettingBox = new System.Windows.Controls.TextBox();
            TempSettingLabel = new System.Windows.Controls.Label();
            TimeSettingLabel = new System.Windows.Controls.Label();

            TempSettingBox.Height = 22;
            TempSettingBox.Width = 55;
            TempSettingBox.Margin = new System.Windows.Thickness(40, 35, 0, 0);
            TempSettingBox.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            TempSettingBox.VerticalAlignment = System.Windows.VerticalAlignment.Top;
            TempSettingBox.Text = Block.TargetTemperature.ToString();
            TempSettingBox.ToolTip = "Change the target temperature";
            Grid.SetColumn(TempSettingBox, ParentCycle.CycleGrid.ColumnDefinitions.Count - 1);
            ParentCycle.CycleGrid.Children.Add(TempSettingBox);

            TempSettingLabel.Height = 26;
            TempSettingLabel.Content = "Temp";
            TempSettingLabel.Margin = new System.Windows.Thickness(2, 33, 0, 0);
            TempSettingLabel.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            TempSettingLabel.VerticalAlignment = System.Windows.VerticalAlignment.Top;
            Grid.SetColumn(TempSettingLabel, ParentCycle.CycleGrid.ColumnDefinitions.Count - 1);
            ParentCycle.CycleGrid.Children.Add(TempSettingLabel);

            TimeSettingBox.Height = 22;
            TimeSettingBox.Width = 55;
            TimeSettingBox.Margin = new System.Windows.Thickness(40, 0, 0, 35);
            TimeSettingBox.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            TimeSettingBox.VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
            TimeSettingBox.Text = Block.TargetTimeSeconds.ToString();
            TimeSettingBox.ToolTip = "Change the target time";
            Grid.SetColumn(TimeSettingBox, ParentCycle.CycleGrid.ColumnDefinitions.Count - 1);
            ParentCycle.CycleGrid.Children.Add(TimeSettingBox);

            TimeSettingLabel.Height = 26;
            TimeSettingLabel.Content = "Time";
            TimeSettingLabel.Margin = new System.Windows.Thickness(2, 0, 0, 33);
            TimeSettingLabel.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            TimeSettingLabel.VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
            Grid.SetColumn(TimeSettingLabel, ParentCycle.CycleGrid.ColumnDefinitions.Count - 1);
            ParentCycle.CycleGrid.Children.Add(TimeSettingLabel);

            TempSettingBox.LostFocus += TempSettingBox_LostFocus;
            TempSettingBox.KeyUp += TempSettingBox_KeyUp;
            TimeSettingBox.LostFocus += TimeSettingBox_LostFocus;
            TimeSettingBox.KeyUp += TimeSettingBox_KeyUp;
        }

        /// <summary>
        /// Update the program if the Enter key is pressed after editing the contents of TimeSettingBox
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TimeSettingBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                updateTimeSettingBox();
            }
        }

        /// <summary>
        /// Update the program if focus is removed from TimeSettingBox after editing the contents of TimeSettingBox
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TimeSettingBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            updateTimeSettingBox();
        }

        /// <summary>
        ///  Update the program according to a modified value in TimeSettingBox
        /// </summary>
        public void updateTimeSettingBox()
        {
            try
            {
                int newVal = Convert.ToInt32(TimeSettingBox.Text);
                if (newVal != Block.TargetTimeSeconds)
                {
                    Block.TargetTimeSeconds = newVal;
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Error: Unable to convert " + TimeSettingBox.Text + " to a time setting.");
            }
        }

        /// <summary>
        /// Update the program if the Enter key is pressed after editing the contents of TempSettingBox
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TempSettingBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                updateTempSettingBox();
            }
        }

        /// <summary>
        /// Update the program if focus is removed from TempSettingBox after editing the contents of TempSettingBox
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TempSettingBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            updateTempSettingBox();
        }

        /// <summary>
        /// Update the program according to a modified value in TempSettingBox
        /// </summary>
        public void updateTempSettingBox()
        {
            try
            {
                double newVal = Convert.ToDouble(TempSettingBox.Text);
                if (newVal != Block.TargetTemperature)
                {
                    Block.TargetTemperature = newVal;
                    ParentCycle.ParentModel.ParentWindow.UpdateEditInterface(ParentCycle.ParentModel.ProgramIndex);
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Error: Unable to convert " + TempSettingBox.Text + " to a temperature setting.");
            }
        }
    }
}
