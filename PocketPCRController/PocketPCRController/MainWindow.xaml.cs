using System;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.IO.Ports;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;

/// <summary>
/// PocketPCRController:  An open source USB serial controller for an open-source and 
/// open-hardware portable USB-powered PCR thermalcycling device - the PocketPCR from GaudiLabs
/// <para>
/// GaudiLabs PocketPCR device: https://gaudi.ch/PocketPCR/
/// </para>
/// <para>
/// This controller couples to a rewrite of the Arduino-based control software for the 
/// GaudiLabs PocketPCR device to allow open-ended programming of the device and computer 
/// control of the device through a USB port.
/// </para>
/// <para>
/// Note: This application requires that the PocketPCR device is first flashed with the
/// PocketPCR_Programmable.ino Arduino code from Tom Hall.
/// </para>
/// <para>
/// The PocketPCR device can be flashed with the Arduino IDE.  The PocketPCR device is based
/// on the Adafruit Feather M0 board, and as such, requires that an entry be made in the 
/// "Additional Boards Manager URLs:" list accessible from the Arduino IDE File->Preferences 
/// menu: Add https://adafruit.github.io/arduino-board-index/package_adafruit_index.json to
/// the list of URLs, then add "Adafruit SAMD Boards" from the Tools->Board->Boards Manager 
/// interface.  After installation, choose "Adafruit Feather M0 from 
/// Tools->Board->Adafruit (32-bits ARM Cortex-M0+ and Cortex-M4) Boards". This should allow
/// flashing updated controller code once the connected COM port is selected under Tools->Port.
/// </para>
/// </summary>
namespace PocketPCRController
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Serial port connection to the PocketPCR device
        /// </summary>
        public SerialPort ComPort { get; set; }
        
        /// <summary>
        /// A list of available COM ports, in case more than one device is connected through a COM port
        /// </summary>
        string[] ports;
        
        /// <summary>
        /// String for keeping track of incoming data on the serial port
        /// </summary>
        string serialOutput = ""; 
        
        /// <summary>
        /// Current position of the virtual PocketPCR rotary dial
        /// </summary>
        double currentRotation;
        
        /// <summary>
        /// A time marker for timing rotary dial clicks (which individually direct a
        /// dial turn) to determine if a double-click has been executed (representing 
        /// a rotary dial button press)
        /// </summary>
        DateTime leftClickedAt;
        
        /// <summary>
        /// Flagged when the left mouse button is clicked
        /// </summary>
        bool leftClicked;

        /// <summary>
        /// Flagged when two left clicks happen in close succession
        /// </summary>
        bool leftDoubleClicked;

        /// <summary>
        /// Flagged when the rotary dial is turned during active thermalcycling
        /// to toggle the option to cancel the currently running cycling program
        /// </summary>
        bool showCancel = false;

        /// <summary>
        /// Rotary dial main selection modes on the PocketPCR device
        /// </summary>
        enum SelectionMode { RunProgram, SelectProgram};

        /// <summary>
        /// Currently selected SelectionMode
        /// </summary>
        SelectionMode currentFunction;

        /// <summary>
        /// The current position on the virtual PocketPCR rotary dial component
        /// </summary>
        int selectorPosition;

        /// <summary>
        /// Currently selected cycling program from the programBox drop-down list
        /// </summary>
        int selectedProgram;

        /// <summary>
        /// Toggled when a cycling program is started or stopped on the PocketPCR device
        /// </summary>
        bool pcrRunning;

        /// <summary>
        /// Title of the currently running thermalcycling program
        /// </summary>
        string runningProgram;

        /// <summary>
        /// Value set when incoming serial data indicates that EEPromSize was requested
        /// </summary>
        int expectingEEPromSize = 0;

        /// <summary>
        /// PCR programs stored on the PocketPCR device
        /// </summary>
        PCRPrograms Programs { get; set; }

        /// <summary>
        /// Time for monitoring the block temperature of a connected device
        /// </summary>
        System.Windows.Forms.Timer monitorTimer { get; set; }

        /// <summary>
        /// Timer for monitoring the state of a running cycling program
        /// </summary>
        System.Windows.Forms.Timer pcrTimer { get; set; }

        /// <summary>
        /// Constructor for a MainWindow
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            currentRotation = 0;
            leftClicked = false;
            leftDoubleClicked = false;
            getAvailableComPorts();
            foreach (string currPort in ports)
            {
                comPortBox.Items.Add(currPort);
            }
            if (ports.Length>0 && ports[0] != null)
            {
                comPortBox.SelectedItem = ports[0];
            }
            ComPort = null;
            runPCRBackground.Background = Brushes.White;
            runPCRLabel.Foreground = Brushes.Black;
            currentFunction = SelectionMode.RunProgram;
            selectorPosition = 0;
            selectedProgram = -1;
            Programs = new PCRPrograms();
            pcrRunning = false;
            runningProgram = "";
            
            btLabel.Visibility = Visibility.Visible;
            blockTempLabel.Visibility = Visibility.Visible;
            runPCRBackground.Visibility = Visibility.Visible;
            programBackground.Visibility = Visibility.Visible;
            runPCRLabel.Visibility = Visibility.Visible;
            programLabel.Visibility = Visibility.Visible;
            runningProgramLabelBackground.Visibility = Visibility.Hidden;
            runningProgramLabel.Visibility = Visibility.Hidden;
            runningBlockLabel.Visibility = Visibility.Hidden;
            totalBlocksLabel.Visibility = Visibility.Hidden;
            separatorLabel.Visibility = Visibility.Hidden;
            setTempLabel.Visibility = Visibility.Hidden;
            runningBlockTempLabel.Visibility = Visibility.Hidden;
            timeLabel.Visibility = Visibility.Hidden;
            closeSerialBtn.IsEnabled = false;
            pcrTimer = new System.Windows.Forms.Timer();
            pcrTimer.Interval = 500;
            pcrTimer.Tick += PcrTimer_Tick;
         //   pcrTimer.Start();
        }

        /// <summary>
        /// Request and display cycling program state information from the PocketPCR device
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PcrTimer_Tick(object sender, EventArgs e)
        {
            if (!pcrRunning) return;
            if (ComPort != null && ComPort.IsOpen)
            {
                serialOutput = "";
                ComPort.Write("QueryRunningPCR\n");
                bool timedOut = false;
                DateTime time = DateTime.Now;
                while (!serialOutput.Contains("pcrState:") && !timedOut)
                {
                    System.Windows.Forms.Application.DoEvents(); // just wait
                    TimeSpan ts = DateTime.Now - time;
                    if (ts.Seconds > 3) timedOut = true;
                }

                if (!timedOut)
                {
                    string[] output = serialOutput.Split(':');
                    serialOutput = "";
                    if (output.Length > 0)
                    {
                        string[] fields = output[1].Split(',');
                        if (fields.Length >= 5)
                        {
                            int cycleAt = Convert.ToInt32(fields[0]);
                            int blockAt = Convert.ToInt32(fields[1]);
                            int cycleRepeatAt = Convert.ToInt32(fields[2]);
                            int overallCycleAt = Convert.ToInt32(fields[3]);
                            int totalSegments = Convert.ToInt32(fields[4]);
                            int totalBlocks = Convert.ToInt32(fields[5]);
                            int cycleRepeats = Convert.ToInt32(fields[6]);
                            int totalCycles = Convert.ToInt32(fields[7]);
                            double targetTemp = Convert.ToDouble(fields[8]);
                            double blockTemp = Convert.ToDouble(fields[9]);
                            int timeSeconds = Convert.ToInt32(fields[10]);
                            runningBlockLabel.Content = "Seg " + (cycleAt + 1).ToString() + "/" + totalSegments.ToString() + ", Blk " + (blockAt + 1).ToString() + "/" + totalBlocks.ToString() + ", Cycle " + (cycleRepeatAt + 1).ToString() + "/" + cycleRepeats.ToString();
                            totalBlocksLabel.Content = "Overall cycle " + (overallCycleAt + 1).ToString() + " of " + totalCycles.ToString();
                            setTempLabel.Content = "Set Temp: " + String.Format("{0:0.00}", targetTemp) + " dC";
                            runningBlockTempLabel.Content = "Block Temp: " + String.Format("{0:0.00}", blockTemp) + " dC";
                            timeLabel.Content = "Time: " + timeSeconds.ToString();
                        }
                    }
                }
                else
                {
                    serialOutput = "";
                }
            }
            System.Windows.Forms.Application.DoEvents();
        }

        /// <summary>
        /// Start a thermalcycling program
        /// </summary>
        /// <param name="pcrString"></param>
        public void startPCR(string pcrString)
        {
            runningProgram = "";
            string[] str = pcrString.Split(':');
            if (str.Length > 1)
            {
                string[] fields = str[1].Split(',');
                if (fields.Length > 1)
                {
                    runningProgram = programBox.Items[Convert.ToInt32(fields[0])].ToString();
                }
            }
            if (runningProgram.Length > 0)
            {
                setupPCR();
            }
        }

        /// <summary>
        /// Set up interface display for displaying active thermalcycling state information
        /// </summary>
        public void setupPCR()
        { 
            selectedProgramLabel.Dispatcher.Invoke((Action)delegate
            {
                selectedProgramLabel.Visibility = Visibility.Hidden;
            }
            );
            runPCRBackground.Dispatcher.Invoke((Action)delegate
            {
                runPCRBackground.Visibility = Visibility.Hidden;
            }
            );
            programBackground.Dispatcher.Invoke((Action)delegate
            {
                programBackground.Visibility = Visibility.Hidden;
            }
            );
            btLabel.Dispatcher.Invoke((Action)delegate
            {
                btLabel.Visibility = Visibility.Hidden;
            }
            );
            blockTempLabel.Dispatcher.Invoke((Action)delegate
            {
                blockTempLabel.Visibility = Visibility.Hidden;
            }
            );
            runPCRLabel.Dispatcher.Invoke((Action)delegate
            {
                runPCRLabel.Visibility = Visibility.Hidden;
            }
            );
            programLabel.Dispatcher.Invoke((Action)delegate
            {
                programLabel.Visibility = Visibility.Hidden;
            }
            );
            runningProgramLabelBackground.Dispatcher.Invoke((Action)delegate
            {
                runningProgramLabelBackground.Visibility = Visibility.Visible;
            }
            );
            runningProgramLabel.Dispatcher.Invoke((Action)delegate
            {
                runningProgramLabel.Visibility = Visibility.Visible;
            }
            );
            runningProgramLabel.Dispatcher.Invoke((Action)delegate
            {
                runningProgramLabel.Content = "PCR Running: " + runningProgram;
            }
            );
            runningBlockLabel.Dispatcher.Invoke((Action)delegate
            {
                runningBlockLabel.Visibility = Visibility.Visible;
            }
            );
            totalBlocksLabel.Dispatcher.Invoke((Action)delegate
            {
                totalBlocksLabel.Visibility = Visibility.Visible;
            }
            );
            separatorLabel.Dispatcher.Invoke((Action)delegate
            {
                separatorLabel.Visibility = Visibility.Visible;
            }
            );
            setTempLabel.Dispatcher.Invoke((Action)delegate
            {
                setTempLabel.Visibility = Visibility.Visible;
            }
            );
            runningBlockTempLabel.Dispatcher.Invoke((Action)delegate
            {
                runningBlockTempLabel.Visibility = Visibility.Visible;
            }
            );
            timeLabel.Dispatcher.Invoke((Action)delegate
            {
                timeLabel.Visibility = Visibility.Visible;
            }
            );
            System.Windows.Forms.Application.DoEvents();
            pcrRunning = true;     
        }

        /// <summary>
        /// Update interface display back to main display and hide thermalcycling program state information labels
        /// </summary>
        public void finishPCR()
        {
            serialOutput = "";
            if (pcrRunning)
            {
                selectedProgramLabel.Dispatcher.Invoke((Action)delegate
                {
                    selectedProgramLabel.Visibility = Visibility.Visible;
                }
                );
                runPCRBackground.Dispatcher.Invoke((Action)delegate
                {
                    runPCRBackground.Visibility = Visibility.Visible;
                }
                );
                programBackground.Dispatcher.Invoke((Action)delegate
                {
                    programBackground.Visibility = Visibility.Visible;
                }
                );
                btLabel.Dispatcher.Invoke((Action)delegate
                {
                    btLabel.Visibility = Visibility.Visible;
                }
                );
                blockTempLabel.Dispatcher.Invoke((Action)delegate
                {
                    blockTempLabel.Visibility = Visibility.Visible;
                }
                );
                runPCRLabel.Dispatcher.Invoke((Action)delegate
                {
                    runPCRLabel.Visibility = Visibility.Visible;
                }
                );
                programLabel.Dispatcher.Invoke((Action)delegate
                {
                    programLabel.Visibility = Visibility.Visible;
                }
                );
                runningProgramLabelBackground.Dispatcher.Invoke((Action)delegate
                {
                    runningProgramLabelBackground.Visibility = Visibility.Hidden;
                }
                );
                runningProgramLabel.Dispatcher.Invoke((Action)delegate
                {
                    runningProgramLabel.Visibility = Visibility.Hidden;
                }
                );
                runningProgramLabel.Dispatcher.Invoke((Action)delegate
                {
                    runningProgramLabel.Content = "";
                }
                );
                runningBlockLabel.Dispatcher.Invoke((Action)delegate
                {
                    runningBlockLabel.Visibility = Visibility.Hidden;
                }
                );
                totalBlocksLabel.Dispatcher.Invoke((Action)delegate
                {
                    totalBlocksLabel.Visibility = Visibility.Hidden;
                }
                );
                separatorLabel.Dispatcher.Invoke((Action)delegate
                {
                    separatorLabel.Visibility = Visibility.Hidden;
                }
                );
                setTempLabel.Dispatcher.Invoke((Action)delegate
                {
                    setTempLabel.Visibility = Visibility.Hidden;
                }
                );
                runningBlockTempLabel.Dispatcher.Invoke((Action)delegate
                {
                    runningBlockTempLabel.Visibility = Visibility.Hidden;
                }
                );
                timeLabel.Dispatcher.Invoke((Action)delegate
                {
                    timeLabel.Visibility = Visibility.Hidden;
                }
                );
                pcrRunning = false;
            }
        }
      
        /// <summary>
        /// Increment virtual PocketPCR rotary dial position
        /// </summary>
        /// <param name="movement"></param>
        private void setSelectorPosition(int movement)
        {
            if (pcrRunning)
            {
                showCancel = !showCancel;
                if (showCancel)
                {
                    runningProgramLabel.Dispatcher.Invoke((Action)delegate
                    {

                        runningProgramLabel.Content = "Press button to stop PCR";
                    }
                    );
                }
                else
                {
                    runningProgramLabel.Dispatcher.Invoke((Action)delegate
                    {
                        runningProgramLabel.Content = "PCR Running: " + runningProgram;
                    }
                    );
                }
                selectorPosition += movement;
                if (selectorPosition < 0) selectorPosition = 1;
                if (selectorPosition > 1) selectorPosition = 0;
            }
            else
            {
                if (currentFunction == SelectionMode.RunProgram)
                {
                    selectorPosition += movement;
                    if (selectorPosition < 0) selectorPosition = 1;
                    if (selectorPosition > 1) selectorPosition = 0;
                    if (selectorPosition == 0)
                    {
                        runPCRBackground.Background = Brushes.White;
                        runPCRLabel.Foreground = Brushes.Black;
                        programBackground.Background = Brushes.Transparent;
                        programLabel.Foreground = Brushes.White;
                    }
                    else
                    {
                        runPCRBackground.Background = Brushes.Transparent;
                        runPCRLabel.Foreground = Brushes.White;
                        programBackground.Background = Brushes.White;
                        programLabel.Foreground = Brushes.Black;
                    }
                }
                if (currentFunction == SelectionMode.SelectProgram)
                {
                    selectorPosition += movement;
                    if (selectorPosition < 0) selectorPosition = programBox.Items.Count - 1;
                    if (selectorPosition > programBox.Items.Count - 1) selectorPosition = 0;
                    selectedProgram = selectorPosition;
                    if (selectedProgram >= 0 && selectedProgram < programBox.Items.Count)
                    {
                        programLabel.Content = programBox.Items[selectedProgram].ToString();
                        programBox.SelectedIndex = selectedProgram;
                    }
                }
            }
        }

        /// <summary>
        /// Change virtual PocketPCR rotary dial position to a specific value
        /// </summary>
        /// <param name="value"></param>
        private void setSelectorPositionAbsolute(int value)
        {
            selectorPosition = value;
            if (pcrRunning)
            {

            }
            else
            {
                if (currentFunction == SelectionMode.RunProgram)
                {
                    if (selectorPosition < 0) selectorPosition = 1;
                    if (selectorPosition > 1) selectorPosition = 0;
                    if (selectorPosition == 0)
                    {
                        runPCRBackground.Dispatcher.Invoke((Action)delegate
                        {
                            runPCRBackground.Background = Brushes.White;
                        }
                        );
                        runPCRLabel.Dispatcher.Invoke((Action)delegate
                        {
                            runPCRLabel.Foreground = Brushes.Black;
                        }
                        );
                        programBackground.Dispatcher.Invoke((Action)delegate
                        {
                            programBackground.Background = Brushes.Transparent;
                        }
                        );
                        programLabel.Dispatcher.Invoke((Action)delegate
                        {
                            programLabel.Foreground = Brushes.White;
                        }
                        );
                    }
                    else
                    {
                        runPCRBackground.Dispatcher.Invoke((Action)delegate
                        {
                            runPCRBackground.Background = Brushes.Transparent;
                        }
                        );
                        runPCRLabel.Dispatcher.Invoke((Action)delegate
                        {
                            runPCRLabel.Foreground = Brushes.White;
                        }
                        );
                        programBackground.Dispatcher.Invoke((Action)delegate
                        {
                            programBackground.Background = Brushes.White;
                        }
                        );
                        programLabel.Dispatcher.Invoke((Action)delegate
                        {
                            programLabel.Foreground = Brushes.Black;
                        }
                        );
                    }
                }
                if (currentFunction == SelectionMode.SelectProgram)
                {
                    if (selectorPosition < 0) selectorPosition = programBox.Items.Count - 1;
                    if (selectorPosition > programBox.Items.Count - 1) selectorPosition = 0;
                    selectedProgram = selectorPosition;
                    if (selectedProgram >= 0 && selectedProgram < programBox.Items.Count)
                    {
                        programLabel.Dispatcher.Invoke((Action)delegate
                        {
                            programLabel.Content = programBox.Items[selectedProgram].ToString();
                        }
                        );
                        programBox.Dispatcher.Invoke((Action)delegate
                        {
                            programBox.SelectedIndex = selectedProgram;
                        }
                        );
                    }
                }
            }
        }

        /// <summary>
        /// Rotate virtual PocketPCR rotary dial counter-clockwise
        /// </summary>
        private void rotateDialLeft()
        {
            // rotate rotary dial counter-clockwise
            currentRotation -= 45;
            RotateTransform rotateTransform = new RotateTransform(currentRotation, 70,70);          
            rotaryDialImage.RenderTransform = rotateTransform;
            leftClicked = false;
            setSelectorPosition(-1);
            if (ComPort!=null && ComPort.IsOpen)
            {
                // ComPort.Write("RotateLeft\n");
                syncDialPositionForward();
            }
        }

        /// <summary>
        /// Rotate virtual PocketPCR rotary dial clockwise
        /// </summary>
        private void rotateDialRight()
        {
            // rotate rotary dial counter-clockwise
            currentRotation += 45;
            RotateTransform rotateTransform = new RotateTransform(currentRotation, 70, 70);
            rotaryDialImage.RenderTransform = rotateTransform;
            setSelectorPosition(1);
            if (ComPort != null && ComPort.IsOpen)
            {
                //ComPort.Write("RotateRight\n");
                syncDialPositionForward();
            }
        }

        /// <summary>
        /// Send serial command to syn the rotary dial with the current virtual position
        /// </summary>
        public void syncDialPositionForward()
        {
            
            if (ComPort != null && ComPort.IsOpen)
            {
                ComPort.Write("SetSelector," + selectorPosition.ToString() + "\n");
            }
        }

        /// <summary>
        /// Send serial command indicating that the rotary dial has been pressed
        /// </summary>
        /// <param name="sendSignalOverSerial"></param>
        private void pushRotaryDial(bool sendSignalOverSerial)
        {
            leftClicked = false;
            if (pcrRunning)
            {
                if (showCancel)
                {
                    showCancel = !showCancel;
                    if (sendSignalOverSerial && ComPort != null && ComPort.IsOpen)
                    {
                        ComPort.Write("PushButtonCancel\n");
                        finishPCR();                          
                    }
                }
            }
            else
            {
                if (currentFunction == SelectionMode.RunProgram)
                {
                    if (selectorPosition == 1)
                    {  // switch to program selection
                        runPCRLabel.Dispatcher.Invoke((Action)delegate
                        {
                            runPCRLabel.Visibility = Visibility.Hidden;
                        }
                        );
                        runPCRBackground.Dispatcher.Invoke((Action)delegate
                        {
                            runPCRBackground.Visibility = Visibility.Hidden;
                        }
                        );
                        programBackground.Dispatcher.Invoke((Action)delegate
                        {
                            programBackground.Visibility = Visibility.Hidden;
                        }
                        );
                        programLabel.Dispatcher.Invoke((Action)delegate
                        {
                            programLabel.Margin = new Thickness(403, 170, 0, 0);
                        }
                        );
                        programLabel.Dispatcher.Invoke((Action)delegate
                        {
                            programLabel.Foreground = Brushes.White;
                        }
                        );
                        programLabel.Dispatcher.Invoke((Action)delegate
                        {
                            programLabel.FontSize = 18;
                        }
                        );
                        if (selectedProgram >= 0 && selectedProgram < programBox.Items.Count)
                        {
                            programLabel.Dispatcher.Invoke((Action)delegate
                            {
                                programLabel.Content = programBox.Items[selectedProgram].ToString();
                            }
                            );
                        }
                        selectorPosition = selectedProgram;
                        currentFunction = SelectionMode.SelectProgram;
                        if (sendSignalOverSerial && ComPort != null && ComPort.IsOpen)
                        {
                            ComPort.Write("PushButton\n");
                            ComPort.Write("SetSelector," + selectorPosition.ToString() + "\n");
                        }
                    }
                    else
                    {
                        if (sendSignalOverSerial && selectedProgram >= 0 && selectedProgram < programBox.Items.Count && programBox.Items[selectedProgram].ToString() != "Cancel")
                        {
                            if (ComPort != null && ComPort.IsOpen)
                            {
                                ComPort.Write("PushButton\n");
                            }
                        }
                    }
                }
                else
                {
                    if (currentFunction == SelectionMode.SelectProgram)
                    {
                        runPCRLabel.Dispatcher.Invoke((Action)delegate
                        {
                            runPCRLabel.Visibility = Visibility.Visible;
                        }
                        );
                        runPCRBackground.Dispatcher.Invoke((Action)delegate
                        {
                            runPCRBackground.Visibility = Visibility.Visible;
                        }
                        );
                        programBackground.Dispatcher.Invoke((Action)delegate
                        {
                            programBackground.Visibility = Visibility.Visible;
                        }
                        );
                        programLabel.Dispatcher.Invoke((Action)delegate
                        {
                            programLabel.Margin = new Thickness(403, 180, 0, 0);
                        }
                        );
                        programLabel.Dispatcher.Invoke((Action)delegate
                        {
                            programLabel.FontSize = 24;
                        }
                        );
                        programLabel.Dispatcher.Invoke((Action)delegate
                        {
                            programLabel.Content = "Program";
                        }
                        );
                        runPCRBackground.Dispatcher.Invoke((Action)delegate
                        {
                            runPCRBackground.Background = Brushes.Transparent;
                        }
                        );
                        runPCRLabel.Dispatcher.Invoke((Action)delegate
                        {
                            runPCRLabel.Foreground = Brushes.White;
                        }
                        );
                        programBackground.Dispatcher.Invoke((Action)delegate
                        {
                            programBackground.Background = Brushes.White;
                        }
                        );
                        programLabel.Dispatcher.Invoke((Action)delegate
                        {
                            programLabel.Foreground = Brushes.Black;
                        }
                        );
                        currentFunction = SelectionMode.RunProgram;

                        selectedProgramLabel.Dispatcher.Invoke((Action)delegate
                        {
                            selectedProgramLabel.Content = programBox.Text;
                        }
                        );

                        if (sendSignalOverSerial && ComPort != null && ComPort.IsOpen)
                        {
                            selectorPosition = 1;
                            ComPort.Write("PushButton\n");
                            ComPort.Write("SetSelector,1\n");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// When the left mouse button is clicked, first check and see if a flag was set that
        /// says the left mouse button was already clicked.  If true, it is a double-click.  If 
        /// not, then set a flag that says the left mouse button was pressed.  If 350 ms passes,
        /// then set that flag back to false.
        /// </summary>
        private void waitForDoubleClick()
        {
            if (leftClicked)
            {
                leftDoubleClicked = true;
                pushRotaryDial(true);
            }
            else
            {
                leftClicked = true;
                leftClickedAt = DateTime.Now;
                TimeSpan span = leftClickedAt - leftClickedAt;
                while (span.Milliseconds<350)
                {
                    span = DateTime.Now - leftClickedAt;
                    System.Windows.Forms.Application.DoEvents();
                }
                if (!leftDoubleClicked)
                {
                    rotateDialLeft();
                    leftClicked = false;
                    leftDoubleClicked = false;
                }  
                else
                {
                    leftDoubleClicked = false;
                }            
            }
        }

        /// <summary>
        /// When the left mouse button is pressed, wait for 350 ms to see if it is double-clicked, otherwise treat it like a single mouse click.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void rotaryDial__PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                waitForDoubleClick();
            }
            else
            {
                rotateDialRight();
            }
        }
        
        /// <summary>
        /// Get connected COM ports
        /// </summary>
        void getAvailableComPorts()
        {
            ports = SerialPort.GetPortNames();
        }

        /// <summary>
        /// Request the current block temperature from the PocketPCR device
        /// </summary>
        /// <returns></returns>
        public double checkBlockTemperature()
        {
            double blockTemp = -1;
            if (ComPort != null && ComPort.IsOpen)
            {
                serialOutput = "";
                ComPort.Write("ReadTemp\n");
                bool timedOut = false;
                DateTime time = DateTime.Now;
                while (!serialOutput.Contains("temp:") && !timedOut)
                {
                    System.Windows.Forms.Application.DoEvents(); // just wait
                    TimeSpan ts = DateTime.Now - time;
                    if (ts.Seconds > 2) timedOut = true;
                }

                if (!timedOut)
                {
                    try
                    {
                        string[] output = serialOutput.Split(':');
                        string temperatureStr = output[1];
                        serialOutput = "";
                        return Convert.ToDouble(temperatureStr);
                    }
                    catch (Exception ex)
                    {
                        serialOutput = "";
                    }
                }
                else
                {
                    serialOutput = "";
                    return -1; 
                }
            }
            return blockTemp;
        }

        /// <summary>
        /// Start a timer to monitor a connected PocketPCR device block temperature
        /// </summary>
        public void monitorBlockTemperature()
        {
            monitorTimer=new System.Windows.Forms.Timer();
            monitorTimer.Interval = 1000;
            monitorTimer.Tick += MonitorTimer_Tick;
            monitorTimer.Start();
        }

        /// <summary>
        /// Check the connected PocketPCR device block temperature
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MonitorTimer_Tick(object sender, EventArgs e)
        {
            if (pcrRunning) return;
            double currentTemp = checkBlockTemperature();
            blockTempLabel.Content = String.Format("{0:0.00}", currentTemp) + " dC";
            System.Windows.Forms.Application.DoEvents();
        }

        /// <summary>
        /// Request the list of programs from a connected PocketPCR device
        /// </summary>
        public void loadPrograms()
        {
            if (ComPort != null && ComPort.IsOpen)
            {
                ComPort.Write("GetEEPROMSize\n");
                bool timedOut = false;
                DateTime time = DateTime.Now;
                while (!serialOutput.Contains("EEPROMSize:") && !timedOut)
                {
                    System.Windows.Forms.Application.DoEvents(); // just wait
                    TimeSpan ts = DateTime.Now - time;
                    if (ts.Seconds > 2) timedOut = true;
                }
                if (!timedOut)
                {
                    string[] output = serialOutput.Split(':');
                    if (output.Length == 2)
                    {
                        expectingEEPromSize= Convert.ToInt32(output[1]);
                        ComPort.Write("GetEEPROM\n");
                    }                       
                }
            }
        }

        /// <summary>
        /// Add currently loaded programs to the programs drop-down list
        /// </summary>
        public void fillProgramList()
        {
            programBox.Dispatcher.Invoke((Action)delegate
            {
                programBox.Items.Clear();
            }
            );
            for (int i = 0; i < Programs.ProgramList.Count; i++)
            {
                if (Programs.ProgramList[i].ProgramName.ToString().Length > 0)
                {      
                    programBox.Dispatcher.Invoke((Action)delegate
                    {
                        programBox.Items.Add(Programs.ProgramList[i].ProgramName.ToString());
                    }
                    );
                }
            }
            if (programBox.Items.Count > 0)
            {
                if (programBox.Items[0].ToString() != "Cancel")
                {
                    programBox.Dispatcher.Invoke((Action)delegate
                    {
                        programBox.SelectedIndex = 0;
                    }
                    );
                }
                else
                {
                    if (programBox.Items.Count > 1)
                    {
                        programBox.Dispatcher.Invoke((Action)delegate
                        {
                            programBox.SelectedIndex = 1;
                        }
                        );
                    }
                }
                programBox.Dispatcher.Invoke((Action)delegate
                {
                    selectedProgram = programBox.SelectedIndex;
                }
                );
                
                selectedProgramLabel.Dispatcher.Invoke((Action)delegate
                {
                    selectedProgramLabel.Content = programBox.Text;
                }
                ); 
            }
        }

        /// <summary>
        /// Connect to a PocketPCR device through a serial port
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void openSerialBtn_Click(object sender, RoutedEventArgs e)
        {
            ComPort = new SerialPort(comPortBox.Text, Convert.ToInt32(baudRateBox.Text), Parity.None, 8, StopBits.One);
            ComPort.DtrEnable = true;
            ComPort.DataReceived += ComPort_DataReceived;
            ComPort.Open();
            if (ComPort.IsOpen)
            {
                loadPrograms();
                double currentTemp = checkBlockTemperature();
                blockTempLabel.Content = String.Format("{0:0.00}", currentTemp) + " dC";
                System.Windows.Forms.Application.DoEvents();
                monitorBlockTemperature();
                closeSerialBtn.IsEnabled = true;
                openSerialBtn.IsEnabled = false;
                setBlockTempBtn.IsEnabled = true;
                pcrTimer.Start();
            }
        }

        /// <summary>
        /// Disconnect from a PocketPCR device currently connected through a COM port
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void closeSerialBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ComPort != null)
            {
                ComPort.Close();
                ComPort = null;
                pcrTimer.Stop();
                programBox.Items.Clear();
                turnOffBlockBtn.IsEnabled = false;
                setBlockTempBtn.IsEnabled = false;
            }
            if (monitorTimer != null)
            {
                monitorTimer.Stop();
                monitorTimer.Dispose();
            }
            openSerialBtn.IsEnabled = false;
            openSerialBtn.IsEnabled = true;
            setBlockTempBtn.IsEnabled = false;
        }

        /// <summary>
        /// Combine two bytes into an unsigned short integer
        /// </summary>
        /// <param name="byte1"></param>
        /// <param name="byte2"></param>
        /// <returns></returns>
        ushort bytesToShort(byte byte1, byte byte2)
        {
            return (ushort)(byte1 * 256 + byte2);
        }

        /// <summary>
        /// Combine two bytes into a 16-bit floating point number to two decimal place precision
        /// <para>
        /// This is not a standard 16-bit floating point conversion.  Instead, two decimal place
        /// precision is achieved by converting a floating point number to a short corresponding
        /// to 100X the original number.  To convert back, divide by 100.
        /// </para>
        /// </summary>
        /// <param name="byte1"></param>
        /// <param name="byte2"></param>
        /// <returns></returns>
        float bytesToFloat(byte byte1, byte byte2)
        {
            return (float)(((double)(byte1 * 256 + byte2)) / 100.0);
        }

        /// <summary>
        /// Covert an unsigned short integer into two bytes
        /// </summary>
        /// <param name="inNumber"></param>
        /// <returns></returns>
        public byte[] shortToBytes(int inNumber)
        {
            // creates a two-byte array that is returned.  
            // The calling method is responsible for freeing the memory
            short byte1 = (short)(inNumber / 256);
            short byte2 = (short)(inNumber - (byte1 * 256));
            byte[] retVal = new byte[2];
            retVal[0] = (byte)byte1;
            retVal[1] = (byte)byte2;
            return retVal;
        }

        /// <summary>
        /// Convert a floating point number into a two bytes
        /// <para>
        /// This is not a standard 16-bit floating point conversion.  Instead, two decimal place
        /// precision is achieved by converting a floating point number to a short corresponding
        /// to 100X the original number.  To convert back, divide by 100.
        /// </para>
        /// </summary>
        /// <param name="inNumber"></param>
        /// <returns></returns>
        public byte[] floatToBytes(double inNumber)
        {
            // creates a two-byte array that is returned.  
            // The calling method is responsible for freeing the memory
            short shortVal = (short)(inNumber * 100.0);
            short byte1 = (short)(shortVal / 256);
            short byte2 = (short)(shortVal - (byte1 * 256));
            byte[] retVal = new byte[2];
            retVal[0] = (byte)byte1;
            retVal[1] = (byte)byte2;
            return retVal;
        }

        /// <summary>
        /// Encode current programs in memory into a byte stream for saving to PocketPCR EEProm.
        /// </summary>
        public void encodeAndTransmitPrograms()
        {
            int maxEEPROM = 0;
            ComPort.Write("GetMaxEEPROMBuffer\n");
            bool timedOut = false;
            DateTime time = DateTime.Now;
            while (!serialOutput.Contains("MAXEEPROM:") && !timedOut)
            {
                System.Windows.Forms.Application.DoEvents(); // just wait
                TimeSpan ts = DateTime.Now - time;
                if (ts.Seconds > 10) timedOut = true;
            }
            if (!timedOut)
            {
                string[] output = serialOutput.Split(':');
                serialOutput = "";
                if (output.Length == 2)
                {
                    try
                    {
                        maxEEPROM = Convert.ToInt32(output[1]);
                    }
                    catch (Exception ex)
                    {
                        System.Windows.Forms.MessageBox.Show("Error:  Unable to retrieve Max EEProm length correctly.\nPlease try again");
                    }
                }
            }
            else
            {
                System.Windows.Forms.MessageBox.Show("Error:  Unable to retrieve Max EEProm length before timing out.\nPlease try again");
                return;
            }
            if (maxEEPROM > 0)
            {
                int numberOfBytesNeeded = 3; // For settings written code and size of EEPROM data
                int i, j, k, m;
                int setAt = 0;
                numberOfBytesNeeded++;  // for number of programs (one byte)
                for (i = 0; i < Programs.ProgramList.Count; i++)
                {
                    numberOfBytesNeeded++; // for length of program name (one byte)
                    numberOfBytesNeeded += Programs.ProgramList[i].ProgramName.Length; // length of name (char array)
                    numberOfBytesNeeded++; // for number of cycling blocks (one byte)
                    for (j = 0; j < Programs.ProgramList[i].Cycles.Count; j++)
                    {
                        numberOfBytesNeeded++; // For number of blocks (one byte)
                        numberOfBytesNeeded++; // For number of times to repeat cycle (one byte)
                        numberOfBytesNeeded += Programs.ProgramList[i].Cycles[j].Blocks.Count * 2; // for temperatures (need to convert float <--> short)
                        numberOfBytesNeeded += Programs.ProgramList[i].Cycles[j].Blocks.Count * 2; // for times
                    }
                }

                if (numberOfBytesNeeded < maxEEPROM) 
                { // Do not allow storage of more than will fit in MAX_SETTINGS bytes
                    byte[] settings = new byte[numberOfBytesNeeded];
                    settings[0] = 254; // code indicating that a program structure has been written to the EEProm   
                    setAt = 3;
                    settings[setAt] = (byte)Programs.ProgramList.Count;
                    setAt++;
                    for (i = 0; i < Programs.ProgramList.Count; i++)
                    {
                        int titleLen = Programs.ProgramList[i].ProgramName.Length;
                        settings[setAt] = (byte)titleLen;
                        setAt++;
                        for (j = 0; j < titleLen; j++)
                        {
                            settings[setAt] = (byte)Programs.ProgramList[i].ProgramName[j];
                            setAt++;
                        }
                        settings[setAt] = (byte)Programs.ProgramList[i].Cycles.Count;
                        setAt++;
         
                        for (j = 0; j < Programs.ProgramList[i].Cycles.Count; j++)
                        {
                            settings[setAt] = (byte)Programs.ProgramList[i].Cycles[j].Blocks.Count;
                            setAt++;
                            settings[setAt] = (byte)Programs.ProgramList[i].Cycles[j].NumberOfCycles;
                            setAt++;
                            for (k = 0; k < Programs.ProgramList[i].Cycles[j].Blocks.Count; k++)
                            {
                                byte[] floatBytes = floatToBytes(Programs.ProgramList[i].Cycles[j].Blocks[k].TargetTemperature);
                                settings[setAt] = floatBytes[0];
                                setAt++;
                                settings[setAt] = floatBytes[1];
                                setAt++;
                                byte[] shortBytes = shortToBytes(Programs.ProgramList[i].Cycles[j].Blocks[k].TargetTimeSeconds);
                                settings[setAt] = shortBytes[0];
                                setAt++;
                                settings[setAt] = shortBytes[1];
                                setAt++;
                            } 
                        }
                    }
                    byte[] sizeBytes = shortToBytes(setAt);
                    settings[1] = sizeBytes[0];
                    settings[2] = sizeBytes[1];
                    ComPort.Write("SendEEPROM\n");
                    ComPort.Write(settings, 0, setAt);
                    System.Windows.Forms.MessageBox.Show("Sent " + setAt.ToString() + " bytes to EEProm.");
                    return;
                }
                else
                {
                    System.Windows.Forms.MessageBox.Show("Error:  Required storage buffer is too big.\nPrograms need to be edited or deleted to lower space requirements.\nRequired space: " + numberOfBytesNeeded.ToString() + " bytes.  Maximum space: " + maxEEPROM + " bytes.");
                }
            }
            else
            {
                System.Windows.Forms.MessageBox.Show("Error:  Unable to retrieve Max EEProm length before timing out.\nPlease try again");
            }
        }

        /// <summary>
        /// Decode a PocketPCR EEProm byte stream into a set of PCRProgram objects
        /// </summary>
        /// <param name="buffer"></param>
        public void createProgramSet(byte[] buffer)
        {
            if (buffer.Length < 4) return;
            int numPrograms = buffer[3];
            int setAt = 4;
            Programs = new PCRPrograms();
            for (int i = 0; i < numPrograms; i++)
            {
                string newTitle = "";
                int titleLen = buffer[setAt];
                setAt++;
                for (int j = 0; j < titleLen; j++)
                {
                    newTitle += (char)buffer[setAt];
                    setAt++;
                }
                PCRProgram newProgram = new PCRProgram(newTitle);
                Programs.ProgramList.Add(newProgram);
                int numCycleBlocks = (int)buffer[setAt];
                newProgram.NumberOfCycles = numCycleBlocks;
                setAt++;
                for (int j = 0; j < numCycleBlocks; j++)
                {
                    PCRCycle newCycle = new PCRCycle();
                    int numBlocks= (int)buffer[setAt];
                    setAt++;
                    newCycle.NumberOfCycles = (int)buffer[setAt];
                    setAt++;
                    for (int k=0; k< numBlocks; k++)
                    {
                        byte byte1 = buffer[setAt];
                        setAt++;
                        byte byte2 = buffer[setAt];
                        setAt++;
                        double targetTemp = bytesToFloat(byte1, byte2);

                        byte1 = buffer[setAt];
                        setAt++;
                        byte2 = buffer[setAt];
                        setAt++;
                        int targetTime = bytesToShort(byte1, byte2);
                        newCycle.Add(targetTemp, targetTime);
                    }
                    newProgram.Cycles.Add(newCycle);
                }
            }
            Programs.BuildDictionary();
            fillProgramList();
        }

        /// <summary>
        /// Data has appeared on the connected COM port
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ComPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (expectingEEPromSize > 0)
            {
                byte[] byteBuffer = new byte[expectingEEPromSize];
                ComPort.Read(byteBuffer, 0, expectingEEPromSize);
                createProgramSet(byteBuffer);
                expectingEEPromSize = 0;
            }
            else
            { 
                serialOutput += ComPort.ReadExisting();
                if (serialOutput.StartsWith("counter:"))
                {
                    string[] str = serialOutput.Split(':');
                    if (str.Length > 1)
                    {
                        setSelectorPositionAbsolute(Convert.ToInt32(str[1]));
                    }
                    serialOutput = "";
                    syncDialPositionForward();
                }
                if (serialOutput.StartsWith("Button Pushed"))
                {
                    pushRotaryDial(false);
                    serialOutput = "";
                    syncDialPositionForward();
                }
                if (serialOutput.StartsWith("PCR Done"))
                {
                    finishPCR();
                }
                if (serialOutput.StartsWith("menu:"))
                {
                    string[] str = serialOutput.Split(':');
                    if (str.Length > 1)
                    {
                        int counter = Convert.ToInt32(str[1]);
                        if (counter > 1) counter = 0;
                        if (counter < 0) counter = 1;
                        setSelectorPositionAbsolute(counter);
                    }
                    serialOutput = "";
                }
                if (serialOutput.StartsWith("pcrStart:"))
                {
                    string pcrStr = serialOutput;
                    serialOutput = "";
                    startPCR(pcrStr);
                }
            }
        }

        /// <summary>
        /// Run program buttom was pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void runProgramBtn_Click(object sender, RoutedEventArgs e)
        {
            int programIndex = programBox.SelectedIndex;
            if (ComPort != null && ComPort.IsOpen)
            {
                ComPort.Write("RunProgram," + programIndex.ToString() + "\n");
            }
        }

        /// <summary>
        /// Export current programs in memory to an XML file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void exportProgramsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveFileDialog saveDlg = new SaveFileDialog()
                {
                    Filter = "xml files (*.xml)|*.xml|All Files(*.*)|*"
                };
                saveDlg.OverwritePrompt = true;
                if (saveDlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    using (var stringwriter = new System.IO.StringWriter())
                    {
                        var serializer = new XmlSerializer(Programs.GetType());
                        serializer.Serialize(stringwriter, Programs);
                        FileStream outFile = new FileStream(saveDlg.FileName, FileMode.Create, FileAccess.Write);
                        WriteTextToFile(outFile, stringwriter.ToString());
                        outFile.Close();
                        outFile.Dispose();
                    };
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error saving file: " + ex.Message);
            }
        }

        /// <summary>
        /// Wipe current programs from memory and import programs from an XML file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void importProgramsBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openDlg = new OpenFileDialog()
            {
                Filter = "xml files (*.xml)|*.xml|All Files(*.*)|*"
            };

            if (openDlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string contents = File.ReadAllText(openDlg.FileName);
                using (var stringReader = new System.IO.StringReader(contents))
                {
                    var serializer = new XmlSerializer(typeof(PCRPrograms));
                    Programs = serializer.Deserialize(stringReader) as PCRPrograms;
                };
            }
            fillProgramList();
        }

        /// <summary>
        /// General method to write text to a file
        /// </summary>
        /// <param name="fs">FileStream to write to</param>
        /// <param name="value">Text to write to the FileStream fs</param>
        /// <returns></returns>
        public int WriteTextToFile(FileStream fs, string value)
        {
            try
            {
                // general method to write text onto the end of an existing file	
                if (fs == null) return -1;
                try
                {
                    byte[] info = new UTF8Encoding(true).GetBytes(value);
                    fs.Write(info, 0, info.Length);
                    return info.Length;
                }
                catch (Exception e)
                {
                    return 0;
                }
            }
            catch (Exception ex)
            {
                return 0;
            }
        }

        /// <summary>
        /// Open Window to edit current cycling programs and set selected program to currently selected program
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void editProgramsBtn_Click(object sender, RoutedEventArgs e)
        {
            CyclingEditWindow EditWindow = new CyclingEditWindow(this);
            EditWindow.FillPrograms(Programs);
            EditWindow.Show();
            if (programBox.Items.Count > 0 && EditWindow.programBox.Items.Count > 0)
            {
                EditWindow.programBox.SelectedIndex = programBox.SelectedIndex;
            }
        }

        /// <summary>
        /// Upload currrent cycling programs to an attached PocketPCR device
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void uploadProgramsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Programs.ProgramList.Count == 0) return;
            DialogResult dialogResult = System.Windows.Forms.MessageBox.Show("This will permanently overwrite the programs on the PocketPCR device.\nAre you sure you want to do this?", "Upload Program Confirmation", MessageBoxButtons.YesNo);
            if (dialogResult != System.Windows.Forms.DialogResult.Yes) return;
            encodeAndTransmitPrograms();
        }

        /// <summary>
        /// Set attached PocketPCR device block temperature to a specific target temperature
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void setBlockTempBtn_Click(object sender, RoutedEventArgs e)
        {
            if (setBlockTempBox.Text.Length>0)
            {
                try
                {
                    double setTemp = Convert.ToDouble(setBlockTempBox.Text);
                    if (ComPort != null && ComPort.IsOpen)
                    {
                        ComPort.Write("Block " + setTemp.ToString() + "\n");
                        turnOffBlockBtn.IsEnabled = true;
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Error: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Turn off heat block on attached PocketPCR device
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void turnOffBlockBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ComPort != null && ComPort.IsOpen)
            {
                ComPort.Write("Block Off\n");
            }
        }
    }
}
