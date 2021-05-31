using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PocketPCRController
{
    /// <summary>
    /// Interaction logic for CyclingEditWindow.xaml
    /// <para>
    /// GUI for editing, adding and removing cycling programs 
    /// </para>
    /// </summary>
    public partial class CyclingEditWindow : Window
    {
        public MainWindow ParentWindow { get; set; }  // Window that created this Window
        public PCRPrograms Programs { get; set; } // List of current cycling programs
        public CyclingEditModel EditModel { get; set; } // Model for what to draw on the Edit interface
        public Canvas TransitionCanvas { get; set; } // Drawing Canvas for displaying selected cycling program
        /// <summary>
        /// Public constructor
        /// </summary>
        /// <param name="_parentWindow"></param>
        public CyclingEditWindow(MainWindow _parentWindow)
        {
            InitializeComponent();
            ParentWindow = _parentWindow;
            EditModel = null;
        }

        /// <summary>
        /// Construct drop-down list of cycling programs
        /// </summary>
        /// <param name="_programs"></param>
        public void FillPrograms(PCRPrograms _programs)
        {
            Programs = _programs;
            programBox.Items.Clear();
            if (Programs != null)
            {
                for (int i = 0; i < Programs.ProgramList.Count; i++)
                {
                    programBox.Items.Add(Programs.ProgramList[i].ProgramName);
                }
            }
        }

        /// <summary>
        /// User changed the selected cycling program in the drop-down list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void programBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (programBox.SelectedIndex>=0)
            {
                UpdateEditInterface(programBox.SelectedIndex);
            }
        }

        /// <summary>
        /// Clear and redraw the current selected cycling program
        /// </summary>
        /// <param name="selectedIndex"></param>
        public void UpdateEditInterface(int selectedIndex)
        {
            for (int i= ScrollGrid.Children.Count-1; i>=0; i--)
            {
                ScrollGrid.Children.RemoveAt(i);
            }
            if (selectedIndex > -1 && Programs.ProgramList.Count > 0)
            {
                EditModel = new CyclingEditModel(this, selectedIndex, this.ScrollGrid, Programs.ProgramList[selectedIndex]);
                currentProgramBox.Text = Programs.ProgramList[selectedIndex].ProgramName;
            }
            else
            {
                EditModel = null;
            }
            drawTransitions();
        }

        /// <summary>
        /// Draw transition lines between cycling steps
        /// </summary>
        public void drawTransitions()
        {
            if (EditModel!=null && EditModel.Program != null && EditModel.Program.NumberOfCycles > 0)
            {         
                int i, j, k;
                double temperatureWidth = EditModel.BlockWidth - EditModel.TransitionWidth;
                double margin = EditModel.TransitionWidth / 2.0;
                double yTopOffSet = 60;
                double yBottomOffSet = 50;
                double yRange = 110.0;
                Point leftPoint = new Point(-1, -1);
                Point rightPoint = new Point(-1, -1);
                Point lastPoint = new Point(-1, -1);
                TransitionCanvas = new Canvas();
                TransitionCanvas.Width = ScrollGrid.Width;
                ScrollGrid.Children.Add(TransitionCanvas);

                for (i = 0; i < TransitionCanvas.Children.Count; i++)
                {
                    TransitionCanvas.Children.RemoveAt(i);
                }
                for (i = 0; i < EditModel.Program.Cycles.Count; i++)
                {
                    for (j=0; j< EditModel.Program.Cycles[i].Blocks.Count; j++)
                    {
                        leftPoint.Y = (ScrollGrid.ActualHeight - yTopOffSet) - (EditModel.Program.Cycles[i].Blocks[j].TargetTemperature / yRange) * (ScrollGrid.ActualHeight - yTopOffSet - yBottomOffSet);
                        rightPoint.Y = leftPoint.Y;
                        if (lastPoint.X > -1 && lastPoint.Y > -1)
                        {
                            leftPoint.X = lastPoint.X + EditModel.TransitionWidth;
                            rightPoint.X = leftPoint.X + temperatureWidth;
                            Line transitionLine = new Line();
                            transitionLine.StrokeThickness = 4;
                            transitionLine.X1 = lastPoint.X - 1.5;
                            transitionLine.X2 = leftPoint.X + 1.5;
                            transitionLine.Y1 = lastPoint.Y;
                            transitionLine.Y2 = leftPoint.Y;
                            transitionLine.Stroke = Brushes.Red;
                            TransitionCanvas.Children.Add(transitionLine);
                        }
                        else
                        {
                            leftPoint.X = margin;
                            rightPoint.X = leftPoint.X + temperatureWidth;
                        }
                        Line newTempLine = new Line();
                        newTempLine.StrokeThickness = 4;
                        newTempLine.X1 = leftPoint.X;
                        newTempLine.X2 = rightPoint.X;
                        newTempLine.Y1 = leftPoint.Y;
                        newTempLine.Y2 = rightPoint.Y;
                        newTempLine.Stroke = Brushes.Red;
                        TransitionCanvas.Children.Add(newTempLine);
                        lastPoint.X = rightPoint.X;
                        lastPoint.Y = rightPoint.Y;
                    }   
                }
            }
        }

        /// <summary>
        /// The size of the drawing Canvas has changed and must be redrawn
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ScrollGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (programBox.SelectedIndex >= 0)
            {
                UpdateEditInterface(programBox.SelectedIndex);
            }
        }

        /// <summary>
        /// Update the title of the selected cycling program
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void updateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (programBox.SelectedIndex >= 0)
            {
                if (currentProgramBox.Text != Programs.ProgramList[programBox.SelectedIndex].ProgramName)
                {
                    Programs.ProgramList[programBox.SelectedIndex].ProgramName = currentProgramBox.Text;
                    int currIndex = programBox.SelectedIndex;
                    ParentWindow.fillProgramList();
                    FillPrograms(Programs);
                    programBox.SelectedIndex = currIndex;
                }
            }     
        }

        /// <summary>
        /// User pressed the New Program button
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void newBtn_Click(object sender, RoutedEventArgs e)
        {
            // Add a new program constructed as a default simple cycling program that can be edited
            DialogResult dialogResult = System.Windows.Forms.MessageBox.Show("Add a program?", "New Program Confirmation", MessageBoxButtons.YesNo);
            if (dialogResult != System.Windows.Forms.DialogResult.Yes) return;
            PCRProgram newProgram = new PCRProgram("New Program");
            PCRCycle newCycle = new PCRCycle();
            newCycle.Add(95, 180);
            newCycle.NumberOfCycles = 1;
            newProgram.Cycles.Add(newCycle);
            newCycle = new PCRCycle();
            newCycle.Add(95, 15);
            newCycle.Add(56, 15);
            newCycle.Add(72, 15);
            newCycle.NumberOfCycles = 35;
            newProgram.Cycles.Add(newCycle);
            newCycle = new PCRCycle();
            newCycle.Add(72, 120);
            newCycle.NumberOfCycles = 1;
            newProgram.Cycles.Add(newCycle);
            newCycle = new PCRCycle();
            newCycle.Add(25, 21000);
            newCycle.NumberOfCycles = 1;
            newProgram.Cycles.Add(newCycle);
            newProgram.RecountCycles();
            Programs.ProgramList.Add(newProgram);
            Programs.BuildDictionary();
            ParentWindow.fillProgramList();
            FillPrograms(Programs);
            programBox.SelectedIndex = programBox.Items.Count - 1;
        }

        /// <summary>
        /// User pressed the Remove Program button
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void deleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (programBox.SelectedIndex >= 0)
            {   // Remove the selected program
                DialogResult dialogResult = System.Windows.Forms.MessageBox.Show("Remove selected program?", "Remove Program Confirmation", MessageBoxButtons.YesNo);
                if (dialogResult != System.Windows.Forms.DialogResult.Yes) return;
                Programs.ProgramList.RemoveAt(programBox.SelectedIndex);
                Programs.BuildDictionary();
                int currIndex = programBox.SelectedIndex;
                ParentWindow.fillProgramList();
                FillPrograms(Programs);
                if (currIndex >= 0 && currIndex < programBox.Items.Count)
                {
                    programBox.SelectedIndex = currIndex;
                }
                else
                {
                    programBox.SelectedIndex = 0;
                }
                UpdateEditInterface(programBox.SelectedIndex);
            }
            else
            {
                EditModel = null;
            }
        }
    } 
}
