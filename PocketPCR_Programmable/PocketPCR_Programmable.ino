/*
  Modification of PocketPCR
  Original PocketPCR PCR Thermocycler by GaudiLabs
  Pocket size USB powered PCR Thermo Cycler

  Original Pocket PCR, designs, and software:
  http://gaudi.ch/PocketPCR/
  https://github.com/GaudiLabs/PocketPCR

  Modification by Tom Hall, May 2021
  Modifications:
  --  Replaced setup interface with stored program selection
  --  Made cycling programming open-ended
  --  Store simple indexed format for multiple open-ended programs as a byte array to EEPROM
  --  Incorporated a simple serial interface to allow control from attached computer
  --  C# (Visual Studio 2015 project,.NET Framework 4.7) program called PocketPCRController 
      to edit and run programs from a USB-attached computer.

  This is distributed under the GNU GENERAL PUBLIC LICENSE, Version 3, 29 June 2007 
*/

#include <math.h>
#include "Adafruit_GFX.h"
#include <Adafruit_SSD1306.h>
#include "Rotary.h"
#include "FlashStorage.h"
#include <Fonts/FreeSans9pt7b.h>

// Need to add a couple of libraries to the original for serial control
#include <iostream>
#include <sstream>

// const char VersionString[] = "V1.02 2020";
const char VersionString[] = "vTH2.0 2021"; // Change this to whatever you want it to reflect

//---------------------------------------------------------------------------------------------
// Original parameters from GaudiLabs

// These constants won't change. They're used to give names to the pins used:
const int analogInPin = A2;  // Analog input pin that the temperature sensor is attached to
const int fanPin = 4; // Analog output pin that the fan is attached to
const int heaterPin = 8; // Analog output pin that the LED is attached to
const int lidPin = 9; // Analog output pin that the LED is attached to
const int butPin = A4; // Analog output pin that the button is attached to

const int NTC_B = 3435;
const float NTC_TN = 298.15;
const int NTC_RN = 10000;
const float NTC_R0 = 4.7;

const float temperature_tollerance = 0.5;
// Rotary encoder is wired with the common to ground and the two
// outputs to pins 6 and 7.
#define ENCODER_1 6
#define ENCODER_2 7
Rotary rotary = Rotary(ENCODER_1, ENCODER_2);

#define SCREEN_WIDTH 128 // OLED display width, in pixels
#define SCREEN_HEIGHT 64 // OLED display height, in pixels

// If using software SPI (the default case):
#define OLED_MOSI   20
#define OLED_CLK   21
#define OLED_DC    0 //11
#define OLED_CS    22 //12
#define OLED_RESET 5

Adafruit_SSD1306 display(SCREEN_WIDTH, SCREEN_HEIGHT,
                         OLED_MOSI, OLED_CLK, OLED_DC, OLED_RESET, OLED_CS);

#define CASE_Main 1
#define CASE_Settings 2
#define CASE_EditSettings 3
#define CASE_Run 4
#define CASE_Done 5
#define CASE_SetTemp 6 // Added by T.Hall to allow using PocketPCR as a set temp incubator via serial interface
                       // Also, useful for calibrating block temperature against an independent thermistor

#define PCR_set 1
#define PCR_transition 2
#define PCR_time 3
#define PCR_end 4

#define PIDp 0.5
#define PIDi 0.0001
#define PIDd 0.15

int sensorValue = 0;        // value read from the sensor
int outputValue = 0;        // value output to the PWM (heater)
float temperature = 0;
float temperature_mean = 0;

float sensorResistance = 0;
float sensorVoltage = 0;

boolean editMode = false;
boolean minuteMode = false;

int counter = 0;
int counter_save = 0;

int x = 0;
int caseUX = CASE_Main;
int casePCR = PCR_set;
int MenuItem = 1;
int PCRstate = 0;
int PCRcycle = 1;

int PCRpwm = 0;

float TEMPset;
float TEMPdif;
float TEMPi;
bool PIDIntegration = false;
float TEMPcontrol;
float TEMPdif_a[10];
float TEMPd;
long TEMPclick = 0;
long TIMEclick = 0;
int TIMEcontrol = 0;

int heatPower = 191;
int fanPower = 0;

//---------------------------------------------------------------------------------------------
// T. Hall additions

// parameters and data structures for storing cycling programs
#define DEFAULT_PROGRAM_Count 3 // default for resetting to default program set 

// A default set of programs to allow a "factory reset"
String PROGRAM_Name[DEFAULT_PROGRAM_Count] = { "GEN PCR", "RT-PCR", "Cyc Test" }; 

// Stored cycling program information
typedef struct {
  float TargetTemp;
  unsigned short TargetTimeSeconds; 
} TemperatureStep;

typedef struct {
  byte NumBlocks;
  TemperatureStep* Block; 
  byte CycleRepeatNumber;
} CyclingBlock;

typedef struct {
  char* ProgramName;
  byte NumCyclingBlocks;
  CyclingBlock* Cycle;
  byte OverallCycles;
} CyclingProgram;

typedef struct {
  byte NumPrograms;
  CyclingProgram* Program;
} ProgramsDefinition;

ProgramsDefinition Programs;

int SelectedProgram = 0;
short cycleAt=0;
short blockAt=0;
short cycleRepeatAt=0;
short overallCycleAt=0;
char displayStr[25];

// Repurposed EEProm for storing open-ended programs in a byte array rather than six params for one simple program
#define MAX_SETTINGS 5000 // Length of EEPROM byte buffer for allocation of program storage space
typedef struct {
  byte value[MAX_SETTINGS];
} EEprom;

FlashStorage(my_flash_store, EEprom);
EEprom settings;

bool rotaryTurned = false;
bool rotaryButtonPressed=false;

// For serial interface 
int incomingByte = 0; // for incoming serial data
int commandPos;
char* command;
char* infoOutput;
int outputPos;

// Thermistor recalibration parameters - defaults
#define ThermA 0.0
#define ThermB 1.0
#define ThermC 0.0

// Recalibration parameters for my damaged thermistor
//#define ThermA -0.00018
//#define ThermB 1.08890
//#define ThermC 7.53134
//---------------------------------------------------------------------------------------------

void setup() {

  pinMode(ENCODER_1, INPUT_PULLUP);
  pinMode(ENCODER_2, INPUT_PULLUP);
  pinMode(butPin, INPUT_PULLUP);

  pinMode(fanPin, OUTPUT);
  pinMode(heaterPin, OUTPUT);
  pinMode(lidPin, OUTPUT);

  attachInterrupt(ENCODER_1, rotate, CHANGE);
  attachInterrupt(ENCODER_2, rotate, CHANGE);

  // initialize serial communications at 115200 bps:
  Serial.begin(115200);
  
  display.begin(SSD1306_SWITCHCAPVCC);

  display.clearDisplay();

  display.setTextSize(1);
  display.setTextColor(WHITE);
  display.setCursor(15, 20);
  display.println("Software Version");
  display.setCursor(30, 30);
  display.println(VersionString);

  display.setCursor(15, 45);
  display.println("Left Handed Mode");
  display.dim(false);
  display.display();

  if (!digitalRead(butPin)) {
    while (!digitalRead(butPin));
    display.setRotation(2);
  } else
    display.setRotation(0); // 0 for right handed, 2 for left handed

  // Show image buffer on the display hardware.
  analogReadResolution(12);

  // allocate memory for serial port input and output
  command=new char[100];
  commandPos=0;
  command[0]='\0';
  infoOutput=new char[500];
  infoOutput[0]='\0';
  outputPos=0;

  // Initialize programs from flash memory
  Programs.NumPrograms=0;
  settings = my_flash_store.read(); 
    
  if (settings.value[0] == 254) // Specific code indicating that there are programs stored
  { 
    decodePrograms();
  }
  else {
    ReinitializeDefaultPrograms(); // This will happen when the progam is reflashed to enure default programs are created
  }
}

void loop() // Main loop
{
  /*
   * Simplified to:
   * 1. Process serial port data if there is any waiting
   * 2. Read the block temperature
   * 3. Process current state of thermalcycling or waiting for user input
   */
  if (Serial.available() > 0) {
    processSerialCommands(); 
  }
  readAnalogData();
  processPCRState();
}

void processPCRState() 
{
  switch (caseUX) {
    
    case CASE_Main:
      if (counter > 1) counter = 0;
      if (counter < 0) counter = 1;
      MenuItem = counter;
      if (rotaryTurned) draw_main_display(); // hold the version screen until the rotary dial is turned for the first time
      
      if (!digitalRead(butPin)) {       
        while (!digitalRead(butPin));
        if (MenuItem == 0) {
          caseUX = CASE_Run;
          casePCR = PCR_set;     
          cycleAt=0;    
          blockAt=0; // Replaced PCRState from original code with blockAt (block in open-ended program  
          cycleRepeatAt=0;   
          overallCycleAt=0;  
          counter = 0;  

          // Send state information over serial
          infoOutput[0]='\0';
          sprintf(infoOutput, "pcrStart:%i,%i", SelectedProgram, Programs.Program[SelectedProgram].NumCyclingBlocks);
          Serial.print(infoOutput);
          
        }
        if (MenuItem == 1) {          
          caseUX = CASE_Settings;
          counter = 0;  

          // Send button pushed message over serial
          Serial.print("Button Pushed");
        }
      }
      break;

    case CASE_Settings: // Program selection mode - replaces original interface for programming cycles in the single simple cylcing program
                        // Note that 'case CASE_EditSettings' has been removed
      if (counter>Programs.NumPrograms-1) counter=0;
      if (counter<0) counter=Programs.NumPrograms-1;
      SelectedProgram = counter;
      
      if (!digitalRead(butPin)) 
      {      
        while (!digitalRead(butPin)); 
        caseUX=CASE_Main; 
        SelectedProgram=counter;
        // Send button pushed message over serial
        Serial.print("Button Pushed");
        delay(500);

        // Send counter information over serial
        infoOutput[0]='\0';
        sprintf(infoOutput, "menu:%i", counter);
        Serial.print(infoOutput);
      }
      draw_program_select_display();
      break;
    /* 
     *  Cycling program is running  
     *  This is left almost unchanged, except to reference the running program 
     *  and block, rather than the setting at index PCR_State*2
     */
    case CASE_Run:  

      TEMPdif = TEMPset - temperature_mean;
      TEMPi = TEMPi + (TEMPset - temperature_mean);

      if (millis() - TEMPclick > 200) {
        TEMPclick = millis();
        TEMPd = TEMPdif_a[4] - TEMPdif;
        TEMPdif_a[4] = TEMPdif_a[3];
        TEMPdif_a[3] = TEMPdif_a[2];
        TEMPdif_a[2] = TEMPdif_a[1];
        TEMPdif_a[1] = TEMPdif_a[0];
        TEMPdif_a[0] = TEMPdif;
        //  Serial.println (TEMPd);
      }

      switch (casePCR) {  
        case PCR_set: 
          TEMPset = Programs.Program[SelectedProgram].Cycle[cycleAt].Block[blockAt].TargetTemp;
          TIMEcontrol = Programs.Program[SelectedProgram].Cycle[cycleAt].Block[blockAt].TargetTimeSeconds;
          PIDIntegration = false;
          casePCR = PCR_transition;
          break;
       
        case PCR_transition:
          runPID();
          draw_run_display();
          if (abs(TEMPset - temperature_mean) < temperature_tollerance) {
            PIDIntegration = true;
            TEMPi = 0;
            TIMEclick = millis();
            casePCR = PCR_time;
          }
          break;

        case PCR_time:
          runPID();
          TIMEcontrol = Programs.Program[SelectedProgram].Cycle[cycleAt].Block[blockAt].TargetTimeSeconds - (millis() - TIMEclick) / 1000;
          draw_run_display();

          if (TIMEcontrol <= 0) { 
            blockAt++;
            if (blockAt >= Programs.Program[SelectedProgram].Cycle[cycleAt].NumBlocks) {
              cycleRepeatAt++;
              overallCycleAt++;
              blockAt=0;
              if (cycleRepeatAt >= Programs.Program[SelectedProgram].Cycle[cycleAt].CycleRepeatNumber) {
                cycleAt++;
                cycleRepeatAt=0;
              }
              if (cycleAt >= Programs.Program[SelectedProgram].NumCyclingBlocks) {
                caseUX = CASE_Done;
              }
            }
            casePCR = PCR_set;
          }
          break;
    
        default:
          // Statement(s)
          break;
      }//PCR switch

      break;
    case CASE_SetTemp:  // This was added to allow setting the block temp like a simple heater from a serial command 
      TEMPdif = TEMPset - temperature_mean;
      TEMPi = TEMPi + (TEMPset - temperature_mean);
      TEMPd = TEMPdif_a[4] - TEMPdif;
      TEMPdif_a[4] = TEMPdif_a[3];
      TEMPdif_a[3] = TEMPdif_a[2];
      TEMPdif_a[2] = TEMPdif_a[1];
      TEMPdif_a[1] = TEMPdif_a[0];
      TEMPdif_a[0] = TEMPdif;
      runPID();
      draw_main_display();
      if (abs(TEMPset - temperature_mean) < temperature_tollerance) {
        PIDIntegration = true;
        TEMPi = 0;   
      }
      break;
      
    case CASE_Done:  // Cycling program has finished

      TEMPcontrol = 0;
      setHeater(temperature_mean, TEMPcontrol);

      display.clearDisplay();
      display.setFont(&FreeSans9pt7b);

      display.fillRect(10, 10, 100, 40, 1);
      display.setCursor(15, 30);
      display.setTextColor(0);
      display.println("PCR Done");
      display.display();
      display.setFont();
      Serial.print("PCR Done");
        
      caseUX = CASE_Main;
      counter = MenuItem;
      
      break;
    default:
      // Statement(s)
      break;
  } //switch
}

/*
 * Interface for processing ASCII serial data as specific commands
 * The main loop is interrupted each time data appears on the serial buffer
 * Program flow is redirected here to either append serial data to a 
 * developing command or, when a carriage return is encountered, to finalize
 * and try to execute the ommand, if recognized, then continue the main loop
 * upon exit.
 * 
 * Command parameters are comma-delimited for simlicity.  A little unusal, and
 * a break from white-space delimitation, like on a termanl cmmand line, but
 * this allows for commands with spaces in them and a simple serial format for
 * parameters.  Parameters are not named, but are positionally oriented and the
 * syntax must be known to properly form parameters
 */
void processSerialCommands() 
{
  incomingByte = Serial.read(); 
  if (incomingByte!=10) {  // keep building the command string
    command[commandPos]=(char)incomingByte;
    commandPos++; 
  }
  else {  // carriage return encountered
    if (commandPos>0) { // if command length is >0
      command[commandPos]='\0';
      commandPos=0;   
     
      char* token = strtok(command, ", ");
      char* baseCommand=token;
      char* params[10];
      int numParams=0; 
      // loop through the string to extract all other tokens
      while( token != NULL ) {  // make list of parameters
        token = strtok(NULL, ",");
        params[numParams]=token;
        if (token!=NULL) {
          numParams++;
        }
      }
      if (strcmp(baseCommand, "ReadTemp")==0) {  // Send back current block temperature
        readAnalogData(); 
        outputPos=0;
        infoOutput[0]='\0';
        sprintf(infoOutput, "temp:%f", temperature_mean);
        Serial.print(infoOutput);
      }
      if (strcmp(baseCommand, "ListPrograms")==0) {  // Send back list of programs in memory (names only)
        listPrograms();
      }
      if (strcmp(baseCommand, "RunProgram")==0) { // Run a cycling program
        if (numParams==1) {
          runProgram(atoi(params[0]));   
        }
      }
      if (strcmp(baseCommand, "SetSelector")==0) { // Set the rotary dial to a specific value
        if (numParams==1) {
          counter=atoi(params[0]);   
        }
      }
      if (strcmp(baseCommand, "RotateLeft")==0) { // Rotate the rotary dial counter clockwise
        counter--;
        rotaryTurned = true;   
      }
      if (strcmp(baseCommand, "RotateRight")==0) { // Rotate the rotary dial clockwise
        counter++;
        rotaryTurned = true;   
      }
      if (strcmp(baseCommand, "PushButton")==0) { // Push the rotary dial button
        processSerialButtonClick(false);
      }
      if (strcmp(baseCommand, "PushButtonCancel")==0) {  // Push the rotary dial button in a "turn off" mode
        processSerialButtonClick(true);
      }
      if (strcmp(baseCommand, "QueryRunningPCR")==0) { // Get current cycling program info and state
        infoOutput[0]='\0';
        sprintf(infoOutput, "pcrState:%i,%i,%i,%i,%i,%i,%i,%i,%f,%f,%i", cycleAt, blockAt, cycleRepeatAt, overallCycleAt, Programs.Program[SelectedProgram].NumCyclingBlocks, Programs.Program[SelectedProgram].Cycle[cycleAt].NumBlocks, Programs.Program[SelectedProgram].Cycle[cycleAt].CycleRepeatNumber, Programs.Program[SelectedProgram].OverallCycles, Programs.Program[SelectedProgram].Cycle[cycleAt].Block[blockAt].TargetTemp, temperature_mean, TIMEcontrol);   
        Serial.print(infoOutput);
      }
      if (strcmp(baseCommand, "InitDefaultPrograms")==0) {  // Reinitialize current program list with hard-coded defaults (e.g. soft "factory reset")
        ReinitializeDefaultPrograms();
        Serial.print("Reinitialized");
        Serial.println();
      }
      if (strcmp(baseCommand, "ShowLoadedPrograms")==0) { // List out current programs to serial in human-readable format
        showLoadedPrograms();
      }
      if (strcmp(baseCommand, "GetMaxEEPROMBuffer")==0) { // Send MAX_SETTINGS to serial
        Serial.print("MAXEEPROM:");
        Serial.print(MAX_SETTINGS);
      }
      if (strcmp(baseCommand, "GetEEPROMSize")==0) {  // Send current memory size of encoded programs EEPRom buffer to serial
        short numBytes=bytesToShort(settings.value[1], settings.value[2]);
        Serial.print("EEPROMSize:");
        Serial.print(numBytes);
      }
      if (strcmp(baseCommand, "GetEEPROM")==0) { // Send encoded programs EEPRom byte stream to serial
        short numBytes=bytesToShort(settings.value[1], settings.value[2]);
        Serial.write(settings.value, numBytes);
      }
      if (strcmp(baseCommand, "SendEEPROM")==0) { // Recieve a re-encoded programs byte stream over serial, write it to EEProm and decode it to memory
        int bufAt=0;
        while (Serial.available() > 0) {
          incomingByte = Serial.read(); 
          settings.value[bufAt]=incomingByte;
          bufAt++; 
        }
        if (bufAt>6) {
          my_flash_store.write(settings); 
          clearPrograms();
          decodePrograms();  
        }
      }
      if (strcmp(baseCommand, "Block")==0) { // Set heat block to a specific temperature or turn the heater off
        if (numParams==1) {
          if (strcmp(params[0], "off")==0 || strcmp(params[0], "Off")==0) {
            setHeater(0, 0);
            TEMPcontrol=0;
            caseUX = CASE_Main; 
          }
          else {
            float tTemp = atof(params[0]);   
            goToTemperature(tTemp);
          }
        }
      }
    }
  }
}

// Clear program data from RAM
void clearPrograms()
{
  int i, j;
  for (i=0; i<Programs.NumPrograms; i++){
    delete []Programs.Program[i].ProgramName; 
    for (j=0; j<Programs.Program[i].NumCyclingBlocks; j++) {
      delete []Programs.Program[i].Cycle[j].Block;   
    }
    delete []Programs.Program[i].Cycle; 
  }
  if (Programs.Program) delete []Programs.Program;
}

// Decode EEProm byte stream into cycling program data structures
void decodePrograms() 
{ 
/*
typedef struct {
  float TargetTemp;
  unsigned short TargetTimeSeconds; 
} TemperatureStep;

typedef struct {
  byte NumBlocks;
  TemperatureStep* Block; 
  byte CycleRepeatNumber;
} Cycle;

typedef struct {
  char* ProgramName;
  byte NumCyclingBlocks;
  CyclingBlock* Cycle;
} CyclingProgram;

typedef struct {
  byte NumPrograms;
  CyclingProgram* Program;
} ProgramsDefinition;

ProgramsDefinition Programs;
*/
  Programs.NumPrograms=settings.value[3]; // It's actually stored as a byte because 255 programs cannot be stored anyway
  int setAt=4;
  Programs.Program=new CyclingProgram[Programs.NumPrograms];
  for (int i=0; i<Programs.NumPrograms; i++) {
    int titleLen=(int)settings.value[setAt];
    setAt++;
    Programs.Program[i].ProgramName=new char[titleLen+1];
    for (int j=0; j<titleLen; j++) {
      Programs.Program[i].ProgramName[j]=settings.value[setAt];
      setAt++; 
    }
    Programs.Program[i].OverallCycles=0;
    Programs.Program[i].ProgramName[titleLen]='\0';
    Programs.Program[i].NumCyclingBlocks=settings.value[setAt];  // Also stored as a byte - a program cannot have more than 255 blocks
    setAt++; 
    Programs.Program[i].Cycle = new CyclingBlock[Programs.Program[i].NumCyclingBlocks];
    for (int j=0; j<Programs.Program[i].NumCyclingBlocks; j++) {
      Programs.Program[i].Cycle[j].NumBlocks=settings.value[setAt];
      setAt++; 
      Programs.Program[i].Cycle[j].CycleRepeatNumber=settings.value[setAt];
      setAt++; 
      Programs.Program[i].OverallCycles+=Programs.Program[i].Cycle[j].CycleRepeatNumber;
      Programs.Program[i].Cycle[j].Block = new TemperatureStep[Programs.Program[i].Cycle[j].NumBlocks];
      for (int k=0; k<Programs.Program[i].Cycle[j].NumBlocks; k++) {
        byte byte1=settings.value[setAt];
        setAt++; 
        byte byte2=settings.value[setAt];
        setAt++; 
        Programs.Program[i].Cycle[j].Block[k].TargetTemp = bytesToFloat(byte1, byte2);
        byte1=settings.value[setAt];
        setAt++;  
        byte2=settings.value[setAt];
        setAt++; 
        Programs.Program[i].Cycle[j].Block[k].TargetTimeSeconds=bytesToShort(byte1, byte2);  
      }
    }
  }
}

// List current programs to serial output.
// This can be used with the serial port window in the Arduino IDE
// Or with a custom serial reader
void showLoadedPrograms() 
{
  if (Programs.NumPrograms>0) {
    Serial.print(Programs.NumPrograms);
    Serial.print(" program");
    if (Programs.NumPrograms!=1)  Serial.print("s");
    Serial.print(" loaded.");
    Serial.println(); 
    for (int i=0; i<Programs.NumPrograms; i++) {
      Serial.print("Program ");
      Serial.print(i+1);  
      Serial.print(": ");  
      Serial.print(Programs.Program[i].ProgramName);
      Serial.println(); 
      Serial.print(Programs.Program[i].NumCyclingBlocks);
      Serial.print(" Cycling Block");
      if (Programs.Program[i].NumCyclingBlocks!=1) Serial.print("s");
      Serial.println();
      for (int j=0; j<Programs.Program[i].NumCyclingBlocks; j++) {
        Serial.print("Cycling Block ");
        Serial.print(j+1); 
        Serial.print(": ");
        Serial.print(Programs.Program[i].Cycle[j].NumBlocks);
        Serial.print(" Temperature Block");
        if (Programs.Program[i].Cycle[j].NumBlocks!=1) Serial.print("s");
        Serial.println();
        for (int k=0; k<Programs.Program[i].Cycle[j].NumBlocks; k++) {
          Serial.print("Temperature Block "); 
          Serial.print(k+1); 
          Serial.print(": Temp: ");
          Serial.print(Programs.Program[i].Cycle[j].Block[k].TargetTemp);
          Serial.print(", Time: ");  
          Serial.print(Programs.Program[i].Cycle[j].Block[k].TargetTimeSeconds);
          Serial.println();
        }   
        Serial.print("Repeat ");
        Serial.print(Programs.Program[i].Cycle[j].CycleRepeatNumber);
        Serial.print(" time");
        if (Programs.Program[i].Cycle[j].CycleRepeatNumber!=1) Serial.print("s");
        Serial.println();
      }
    }
  }
  else {
    Serial.print("No programs loaded.");
    Serial.println();
  }
}

// Encode current programs into a byte stream and flash to EEProm
int encodeAndSaveToEEProm() 
{
  /*  return code:
      0 = OK
      >0 = Not enough EEPROM space available (max allocation = 5500 bytes), return value is the overage
  */
/*
typedef struct {
  float TargetTemp;
  unsigned short TargetTimeSeconds; 
} TemperatureStep;

typedef struct {
  byte NumBlocks;
  TemperatureStep* Block; 
  byte CycleRepeatNumber;
} Cycle;

typedef struct {
  char* ProgramName;
  byte NumCyclingBlocks;
  CyclingBlock* Cycle;
} CyclingProgram;

typedef struct {
  byte NumPrograms;
  CyclingProgram* Program;
} ProgramsDefinition;

ProgramsDefinition Programs;
*/

  int numberOfBytesNeeded=3; // For settings written code and size of EEPROM data
  int i, j, k;  
  int setAt=0;
  numberOfBytesNeeded++;  // for number of programs (one byte)
  for (i=0; i<Programs.NumPrograms; i++) {
    numberOfBytesNeeded++; // for length of program name (one byte)
    numberOfBytesNeeded+=strlen(Programs.Program[i].ProgramName); // length of name (char array)
    numberOfBytesNeeded++; // for number of cycling blocks (one byte)
    for (j=0; j<Programs.Program[i].NumCyclingBlocks; j++) {
      numberOfBytesNeeded++; // for number of blocks in the cycle (one byte)   
      numberOfBytesNeeded++; // for CycleRepeatNumber
      numberOfBytesNeeded+=Programs.Program[i].Cycle[j].NumBlocks * 2; // for temperatures (need to convert float <--> short)
      numberOfBytesNeeded+=Programs.Program[i].Cycle[j].NumBlocks * 2; // for times
    }
  }

  if (numberOfBytesNeeded < MAX_SETTINGS) { // Do not allow storage of more than will fit in MAX_SETTINGS bytes
    settings.value[0]=254; // code indicating that a program structure has been written to the EEProm   
    setAt=3;
    settings.value[setAt]=Programs.NumPrograms; 
    setAt++;
    for (i=0; i<Programs.NumPrograms; i++) {
      int titleLen=strlen(Programs.Program[i].ProgramName);
      settings.value[setAt]=(byte)titleLen;
      setAt++;
      for (j=0; j<titleLen; j++) {
        settings.value[setAt]=Programs.Program[i].ProgramName[j];  
        setAt++;
      }
      settings.value[setAt]=Programs.Program[i].NumCyclingBlocks;
      setAt++;
      for (j=0; j<Programs.Program[i].NumCyclingBlocks; j++) {
        settings.value[setAt]=Programs.Program[i].Cycle[j].NumBlocks;
        setAt++;
        settings.value[setAt]=Programs.Program[i].Cycle[j].CycleRepeatNumber;
        setAt++;
        for (k=0; k<Programs.Program[i].Cycle[j].NumBlocks; k++) {
          byte *floatBytes=floatToBytes(Programs.Program[i].Cycle[j].Block[k].TargetTemp);
          settings.value[setAt]=floatBytes[0];
          setAt++;  
          settings.value[setAt]=floatBytes[1];
          setAt++; 
          byte *shortBytes=shortToBytes(Programs.Program[i].Cycle[j].Block[k].TargetTimeSeconds);
          settings.value[setAt]=shortBytes[0];
          setAt++; 
          settings.value[setAt]=shortBytes[1];
          setAt++; 
          delete []floatBytes;
          delete []shortBytes;
        }
      }
    }
    byte *sizeBytes=shortToBytes((unsigned short)setAt);
    settings.value[1]=sizeBytes[0]; 
    settings.value[2]=sizeBytes[1];
    delete []sizeBytes;
    my_flash_store.write(settings);  

    Serial.print("Wrote ");
    Serial.print(setAt);
    Serial.print(" bytes to EEprom.");
    Serial.println();
    Serial.print("Calculated that ");
    Serial.print(numberOfBytesNeeded);
    Serial.print(" bytes were needed."); 
    Serial.println();
    Serial.print(bytesToShort(settings.value[1], settings.value[2]));
    Serial.print(" written to EEPROM positions 1 and 2."); 
    Serial.println();
    Serial.print(settings.value[1]);
    Serial.print(", ");
    Serial.print(settings.value[2]);
    Serial.println();
  }
  else {
    Serial.println("Not enough space");
    Serial.print(numberOfBytesNeeded - MAX_SETTINGS);
    Serial.print(" too many bytes required.");
    Serial.println();
    return numberOfBytesNeeded - MAX_SETTINGS;
  }
}

// Convert a float into two bytes to add to the EEProm byte stream
byte* floatToBytes(float inFloat) 
{
  // creates a two-byte array that is returned.  
  // The calling method is responsible for freeing the memory
  short shortVal=(short)(inFloat*100.00f);
  short byte1=shortVal/256;
  short byte2=shortVal-(byte1*256);
  byte* retVal=new byte[2];
  retVal[0]=(byte)byte1;
  retVal[1]=(byte)byte2; 
  return retVal;
}

// Convert two bytes from the EEProm byte stream into a float 
float bytesToFloat(byte byte1, byte byte2)
{
  return ((float)(((short)byte1*256) + (short)byte2))/100.00f;
}

// Convert an unsigned short into two bytes to add to the EEProm byte stream
byte* shortToBytes(unsigned short inShort) 
{
  // creates a two-byte array that is returned.  
  // The calling method is responsible for freeing the memory
  unsigned short byte1=inShort/256;
  unsigned short byte2=inShort-(byte1*256);
  byte* retVal=new byte[2];
  retVal[0]=(byte)byte1;
  retVal[1]=(byte)byte2; 
  return retVal;
}

// Convert two bytes from the EEProm byte stream into an unsigned short 
unsigned short bytesToShort(byte byte1, byte byte2)
{
  return (unsigned short)(byte1*256) + (unsigned short)byte2;
}

// Create a default set of cycling programs to initialize the device
void ReinitializeDefaultPrograms() 
{
/*
typedef struct {
  float TargetTemp;
  unsigned short TargetTimeSeconds; 
} TemperatureStep;

typedef struct {
  byte NumBlocks;
  TemperatureStep* Block; 
  byte CycleRepeatNumber;
} Cycle;

typedef struct {
  char* ProgramName;
  byte NumCyclingBlocks;
  CyclingBlock* Cycle;
} CyclingProgram;

typedef struct {
  byte NumPrograms;
  CyclingProgram* Program;
} ProgramsDefinition;

ProgramsDefinition Programs;
*/
  clearPrograms();
  int i;
  Programs.NumPrograms = DEFAULT_PROGRAM_Count;
  Programs.Program = new CyclingProgram[DEFAULT_PROGRAM_Count];

  // GEN PCR
  Programs.Program[0].ProgramName=new char[PROGRAM_Name[0].length()+1]; 
  strcpy(Programs.Program[0].ProgramName, PROGRAM_Name[0].c_str());
  Programs.Program[0].ProgramName[PROGRAM_Name[0].length()]='\0';

  Programs.Program[0].NumCyclingBlocks=4;
  Programs.Program[0].Cycle = new CyclingBlock[Programs.Program[0].NumCyclingBlocks];
  Programs.Program[0].OverallCycles=0;
  
  // Denature - Hot start
  Programs.Program[0].Cycle[0].NumBlocks=1;
  Programs.Program[0].Cycle[0].CycleRepeatNumber = 1; // Just do once
  Programs.Program[0].Cycle[0].Block = new TemperatureStep[Programs.Program[0].Cycle[0].NumBlocks];
  Programs.Program[0].Cycle[0].Block[0].TargetTemp = 95;
  Programs.Program[0].Cycle[0].Block[0].TargetTimeSeconds = 3 * 60; // 3 minutes
  Programs.Program[0].OverallCycles++;
  
  // Cycling
  double denatureTemp = 95;
  double annealTemp = 56;
  double extensionTemp = 72;
  int numCycles = 35;
  
  Programs.Program[0].Cycle[1].NumBlocks=3;
  Programs.Program[0].Cycle[1].CycleRepeatNumber = 35; // 35 cycles
  Programs.Program[0].Cycle[1].Block = new TemperatureStep[Programs.Program[0].Cycle[1].NumBlocks];
  
  Programs.Program[0].Cycle[1].Block[0].TargetTemp = denatureTemp;
  Programs.Program[0].Cycle[1].Block[0].TargetTimeSeconds = 20; // 20 seconds denature

  Programs.Program[0].Cycle[1].Block[1].TargetTemp = annealTemp;
  Programs.Program[0].Cycle[1].Block[1].TargetTimeSeconds = 20; // 20 seconds annealing

  Programs.Program[0].Cycle[1].Block[2].TargetTemp = extensionTemp;
  Programs.Program[0].Cycle[1].Block[2].TargetTimeSeconds = 60; // 1 minute extension

  Programs.Program[0].OverallCycles+=numCycles;
  
  // Finish up for 3 minutes
  Programs.Program[0].Cycle[2].NumBlocks=1;
  Programs.Program[0].Cycle[2].CycleRepeatNumber = 1; // Just do once
  Programs.Program[0].Cycle[2].Block = new TemperatureStep[Programs.Program[0].Cycle[2].NumBlocks];
  Programs.Program[0].Cycle[2].Block[0].TargetTemp = 72;
  Programs.Program[0].Cycle[2].Block[0].TargetTimeSeconds = 3 * 60; // 3 minutes
  Programs.Program[0].OverallCycles++;
  
  // Final Hold
  Programs.Program[0].Cycle[3].NumBlocks=1;
  Programs.Program[0].Cycle[3].CycleRepeatNumber = 1; // Just do once
  Programs.Program[0].Cycle[3].Block = new TemperatureStep[Programs.Program[0].Cycle[3].NumBlocks];
  Programs.Program[0].Cycle[3].Block[0].TargetTemp = 25; // No cooler, so no sense targeting a temperature that makes the fan run indefinitely
  Programs.Program[0].Cycle[3].Block[0].TargetTimeSeconds = 360 * 60; // 6 hours - This could be up to 65535 to pretend to be "infinite"
  Programs.Program[0].OverallCycles++;
  
  // ------------------------------------------------------------------------------------
  
  // RT-PCR with a step-down annealing
  Programs.Program[1].ProgramName=new char[PROGRAM_Name[1].length()+1]; 
  strcpy(Programs.Program[1].ProgramName, PROGRAM_Name[1].c_str());
  Programs.Program[1].ProgramName[PROGRAM_Name[1].length()]='\0';

  Programs.Program[1].NumCyclingBlocks=10;

  Programs.Program[1].Cycle = new CyclingBlock[Programs.Program[1].NumCyclingBlocks];
  Programs.Program[1].OverallCycles=0;
  // RT step
  Programs.Program[1].Cycle[0].NumBlocks=1;
  Programs.Program[1].Cycle[0].CycleRepeatNumber = 1; // Just do once
  Programs.Program[1].Cycle[0].Block = new TemperatureStep[Programs.Program[1].Cycle[0].NumBlocks];
  Programs.Program[1].Cycle[0].Block[0].TargetTemp = 54;
  Programs.Program[1].Cycle[0].Block[0].TargetTimeSeconds = 30 * 60; // 30 minutes
  Programs.Program[1].OverallCycles++;
  
  // Denature - Hot start
  Programs.Program[1].Cycle[1].NumBlocks=1;
  Programs.Program[1].Cycle[1].CycleRepeatNumber = 1; // Just do once
  Programs.Program[1].Cycle[1].Block = new TemperatureStep[Programs.Program[1].Cycle[1].NumBlocks];
  Programs.Program[1].Cycle[1].Block[0].TargetTemp = 95;
  Programs.Program[1].Cycle[1].Block[0].TargetTimeSeconds = 3 * 60; // 3 minutes
  Programs.Program[1].OverallCycles++;
  
  // Step-down
  denatureTemp = 95;
  annealTemp = 66;
  extensionTemp = 72;
  double increment=2;
  int bAt=2;
  for (int i=0; i<5; i++) 
  {
    Programs.Program[1].Cycle[bAt].NumBlocks=3;
    Programs.Program[1].Cycle[bAt].CycleRepeatNumber=1;
    Programs.Program[1].Cycle[bAt].Block = new TemperatureStep[Programs.Program[1].Cycle[bAt].NumBlocks];
    Programs.Program[1].Cycle[bAt].Block[0].TargetTemp = denatureTemp;
    Programs.Program[1].Cycle[bAt].Block[0].TargetTimeSeconds = 20;
    Programs.Program[1].Cycle[bAt].Block[1].TargetTemp = annealTemp;
    Programs.Program[1].Cycle[bAt].Block[1].TargetTimeSeconds = 30;
    Programs.Program[1].Cycle[bAt].Block[2].TargetTemp = extensionTemp;
    Programs.Program[1].Cycle[bAt].Block[2].TargetTimeSeconds = 60;  // 1 minute
    annealTemp-=increment;
    bAt++;
  }
  Programs.Program[1].OverallCycles+=5;
  
  // Cycling
  denatureTemp = 95;
  annealTemp = 56;
  extensionTemp = 72;
  numCycles = 35;
  Programs.Program[1].Cycle[bAt].NumBlocks=3;
  Programs.Program[1].Cycle[bAt].CycleRepeatNumber = numCycles; // 35 cycles
  Programs.Program[1].Cycle[bAt].Block = new TemperatureStep[Programs.Program[1].Cycle[bAt].NumBlocks];
  Programs.Program[1].Cycle[bAt].Block[0].TargetTemp = denatureTemp;
  Programs.Program[1].Cycle[bAt].Block[0].TargetTimeSeconds = 20; // 20 seconds denature
  Programs.Program[1].Cycle[bAt].Block[1].TargetTemp = annealTemp;
  Programs.Program[1].Cycle[bAt].Block[1].TargetTimeSeconds = 30; // 20 seconds annealing
  Programs.Program[1].Cycle[bAt].Block[2].TargetTemp = extensionTemp;
  Programs.Program[1].Cycle[bAt].Block[2].TargetTimeSeconds = 60; // 1 minute extension
  bAt++;
  Programs.Program[1].OverallCycles+=numCycles;
  
  // Finish up for 3 minutes
  Programs.Program[1].Cycle[bAt].NumBlocks=1;
  Programs.Program[1].Cycle[bAt].CycleRepeatNumber = 1; // Just do once
  Programs.Program[1].Cycle[bAt].Block = new TemperatureStep[Programs.Program[1].Cycle[bAt].NumBlocks];
  Programs.Program[1].Cycle[bAt].Block[0].TargetTemp = 72;
  Programs.Program[1].Cycle[bAt].Block[0].TargetTimeSeconds = 3 * 60; // 3 minutes
  bAt++;
  Programs.Program[1].OverallCycles++;
  
  // Final Hold
  Programs.Program[1].Cycle[bAt].NumBlocks=1;
  Programs.Program[1].Cycle[bAt].CycleRepeatNumber = 1; // Just do once
  Programs.Program[1].Cycle[bAt].Block = new TemperatureStep[Programs.Program[1].Cycle[bAt].NumBlocks];
  Programs.Program[1].Cycle[bAt].Block[0].TargetTemp = 25; 
  Programs.Program[1].Cycle[bAt].Block[0].TargetTimeSeconds = 360 * 60; 
  bAt++;
  Programs.Program[1].OverallCycles++;
  // ------------------------------------------------------------------------------------
  
  // Test run program with short times - This program can be used to quickly verify that the cycling system is working
  // The associated Computer-side program can be used to delete this program once satisfied that the system works as indicated
  Programs.Program[2].ProgramName=new char[PROGRAM_Name[2].length()+1]; 
  strcpy(Programs.Program[2].ProgramName, PROGRAM_Name[2].c_str());
  Programs.Program[2].ProgramName[PROGRAM_Name[2].length()]='\0';

  Programs.Program[2].NumCyclingBlocks=10;
  
  Programs.Program[2].OverallCycles=0;
  Programs.Program[2].Cycle = new CyclingBlock[Programs.Program[2].NumCyclingBlocks];
  
  // RT step
  Programs.Program[2].Cycle[0].NumBlocks=1;
  Programs.Program[2].Cycle[0].CycleRepeatNumber = 1; // Just do once
  Programs.Program[2].Cycle[0].Block = new TemperatureStep[Programs.Program[2].Cycle[0].NumBlocks];
  Programs.Program[2].Cycle[0].Block[0].TargetTemp = 54;
  Programs.Program[2].Cycle[0].Block[0].TargetTimeSeconds = 2; // 30 minutes
  Programs.Program[2].OverallCycles++;
  
  // Denature - Hot start
  Programs.Program[2].Cycle[1].NumBlocks=1;
  Programs.Program[2].Cycle[1].CycleRepeatNumber = 1; // Just do once
  Programs.Program[2].Cycle[1].Block = new TemperatureStep[Programs.Program[2].Cycle[1].NumBlocks];
  Programs.Program[2].Cycle[1].Block[0].TargetTemp = 65; // to shorten time
  Programs.Program[2].Cycle[1].Block[0].TargetTimeSeconds = 2; // 3 minutes
  Programs.Program[2].OverallCycles++;
  
  // Step-down
  denatureTemp = 64;
  annealTemp = 60;
  extensionTemp = 62;
  increment=2;
  bAt=2;
  for (int i=0; i<5; i++) 
  {
    Programs.Program[2].Cycle[bAt].NumBlocks=3;
    Programs.Program[2].Cycle[bAt].CycleRepeatNumber=1;
    Programs.Program[2].Cycle[bAt].Block = new TemperatureStep[Programs.Program[2].Cycle[bAt].NumBlocks];
    Programs.Program[2].Cycle[bAt].Block[0].TargetTemp = denatureTemp;
    Programs.Program[2].Cycle[bAt].Block[0].TargetTimeSeconds = 1;
    Programs.Program[2].Cycle[bAt].Block[1].TargetTemp = annealTemp;
    Programs.Program[2].Cycle[bAt].Block[1].TargetTimeSeconds = 1;
    Programs.Program[2].Cycle[bAt].Block[2].TargetTemp = extensionTemp;
    Programs.Program[2].Cycle[bAt].Block[2].TargetTimeSeconds = 1;  // 1 minute
    annealTemp-=increment;
    bAt++;
  }
  Programs.Program[2].OverallCycles+=5;
  
  // Cycling
  denatureTemp = 64;
  annealTemp = 54;
  extensionTemp = 62;
  numCycles = 5;
  Programs.Program[2].Cycle[bAt].NumBlocks=3;
  Programs.Program[2].Cycle[bAt].CycleRepeatNumber = numCycles; // 5 test cycles
  Programs.Program[2].Cycle[bAt].Block = new TemperatureStep[Programs.Program[2].Cycle[bAt].NumBlocks];
  Programs.Program[2].Cycle[bAt].Block[0].TargetTemp = denatureTemp;
  Programs.Program[2].Cycle[bAt].Block[0].TargetTimeSeconds = 1; // 20 seconds denature
  Programs.Program[2].Cycle[bAt].Block[1].TargetTemp = annealTemp;
  Programs.Program[2].Cycle[bAt].Block[1].TargetTimeSeconds = 1; // 20 seconds annealing
  Programs.Program[2].Cycle[bAt].Block[2].TargetTemp = extensionTemp;
  Programs.Program[2].Cycle[bAt].Block[2].TargetTimeSeconds = 1; // 1 minute extension
  bAt++;
  Programs.Program[2].OverallCycles+=numCycles;
  
  // Finish up 
  Programs.Program[2].Cycle[bAt].NumBlocks=1;
  Programs.Program[2].Cycle[bAt].CycleRepeatNumber = 1; // Just do once
  Programs.Program[2].Cycle[bAt].Block = new TemperatureStep[Programs.Program[2].Cycle[bAt].NumBlocks];
  Programs.Program[2].Cycle[bAt].Block[0].TargetTemp = 62;
  Programs.Program[2].Cycle[bAt].Block[0].TargetTimeSeconds = 1; // 3 minutes
  bAt++;
  Programs.Program[2].OverallCycles++;
  
  // Final Hold
  Programs.Program[2].Cycle[bAt].NumBlocks=1;
  Programs.Program[2].Cycle[bAt].CycleRepeatNumber = 1; // Just do once
  Programs.Program[2].Cycle[bAt].Block = new TemperatureStep[Programs.Program[2].Cycle[bAt].NumBlocks];
  Programs.Program[2].Cycle[bAt].Block[0].TargetTemp = 25; 
  Programs.Program[2].Cycle[bAt].Block[0].TargetTimeSeconds = 1000; 
  bAt++;
  Programs.Program[2].OverallCycles++;
  
  encodeAndSaveToEEProm();
}

/*
 * Read block temperature
 * THis was modified to include a second recalibration if necessary.  Currently set to not change determined temp
 */
void readAnalogData()
{
  // read the analog in value:
  // analogReference(AR_EXTERNAL);
  setHeater(0, 0);
  sensorValue = analogRead(analogInPin);
  setHeater(temperature_mean, TEMPcontrol);
  sensorVoltage = 3.3 * sensorValue / 4096;
  sensorResistance = ((sensorVoltage * NTC_R0) / (3.3 - sensorVoltage));
  float uncorrected_temperature =  1 / (log(sensorResistance * 1000 / NTC_RN) / NTC_B + 1 / NTC_TN) - 273.15 ;
  
  /* 
   *  THIS WAS A CUSTOM ADJUSTMENT DUE TO OVERHEATING THE THERMISTOR!
   *  Overpowering the board with an iPhone charger caused the readings to freeze with the block heater on and it went > 130 dC, possibly a bit higher
   *  Thermistor probably deformed, changing resistance
   *  A calibration was perfomed against an independent micro thermistor probe and fit to a second-order polynomial
   *  Rather than commenting it out, it is left here with parameters that correspond 1:1
   *  If an independent calibration is performed, a 2nd order polynomial can be fit and parameters substituted here
   *  Recalibration parameters for one particular damaged thermistor:
   *  #define ThermA -0.00018
   *  #define ThermB 1.08890
   *  #define ThermC 7.53134
  */
   
   temperature = (uncorrected_temperature * uncorrected_temperature * ThermA) + (uncorrected_temperature * ThermB) + ThermC;
   temperature_mean = (temperature_mean * 3 + temperature) / 4; 
}

// Set block to a particular temperature by modifying parameters for the processPCRState routine
void goToTemperature(float target) 
{
  TEMPset = target;
  caseUX = CASE_SetTemp;
}

/*
 * Process a serial command to emulate a rotary dial button press
 */
void processSerialButtonClick(bool cancelPCR) 
{
  if (cancelPCR) {
    caseUX=CASE_Done;   
  }
  else {
    switch (caseUX) {
      case CASE_Main:
        if (counter > 1) counter = 0;
        if (counter < 0) counter = 1;
        MenuItem = counter;
        if (rotaryTurned) draw_main_display();
          if (MenuItem == 0) {
            caseUX = CASE_Run;
            casePCR = PCR_set;         
            blockAt=0; 
            cycleAt=0;
            cycleRepeatAt=0;  
            overallCycleAt=0;      
            counter = 0;  
            infoOutput[0]='\0';
            sprintf(infoOutput, "pcrStart:%i,%i", SelectedProgram, Programs.Program[SelectedProgram].NumCyclingBlocks);
            Serial.print(infoOutput);
          }
          if (MenuItem == 1) {          
            caseUX = CASE_Settings;
            counter = 1;   
          }
        break;

      case CASE_Settings:
        if (counter>Programs.NumPrograms-1) counter=0;
        if (counter<0) counter=Programs.NumPrograms-1;
        SelectedProgram = counter;  
          caseUX=CASE_Main; 
          SelectedProgram=counter;
        draw_program_select_display();
        break;
      default:
        // Statement(s)
        break;
    } //switch
  }
}

// Run a PCR program by modifying parameters for the processPCRState routine 
void runProgram(int programNumber) 
{
  SelectedProgram=programNumber;
  caseUX = CASE_Run;
  casePCR = PCR_set;
  blockAt=0;
  cycleAt=0;
  cycleRepeatAt=0;
  overallCycleAt=0;
  counter = 0; 
  infoOutput[0]='\0';
  sprintf(infoOutput, "pcrStart:%i,%i", SelectedProgram, Programs.Program[SelectedProgram].NumCyclingBlocks);
  Serial.print(infoOutput);  
}

// List out current cycling program names
void listPrograms() 
{
  outputPos=0;
  infoOutput[0]='\0'; 
  int i, j, k; 
  sprintf(infoOutput, "programs:");
  k=9;
  if (Programs.NumPrograms>0) {
    for (i=0; i<Programs.NumPrograms; i++) {
      for (j=0; j<strlen(Programs.Program[i].ProgramName); j++) {
        infoOutput[k]=Programs.Program[i].ProgramName[j];
        k++;    
      }
      infoOutput[k]=',';
      k++;
    }
    infoOutput[k]='\0';
    Serial.print(infoOutput);
    Serial.println();
  }
  else {
    Serial.print("No programs loaded");
    Serial.println();
  }
}

// Draw the display when program selection is active
void draw_program_select_display()
{
  display.clearDisplay();
  display.setTextSize(2);
  display.setTextColor(WHITE);
  display.setCursor(10, 30);
  if (SelectedProgram<Programs.NumPrograms) {
    display.println(Programs.Program[SelectedProgram].ProgramName);
  }
  display.display();
}

// Draw the display when no program is running and not in selection mode
void draw_main_display()
{
  display.clearDisplay();
  display.setTextSize(1);
  display.setTextColor(WHITE);
  display.setCursor(1, 1);
  if (SelectedProgram<Programs.NumPrograms) {
    display.println(Programs.Program[SelectedProgram].ProgramName);
  }
  else {
    display.println("No program selected");
  }
  display.setCursor(85, 1);
  display.print(temperature_mean, 1);
  display.setFont(&FreeSans9pt7b);
  if (MenuItem == 0) // Set or Cancel
  {
    display.fillRect(21, 10, 86, 25, 1);
    display.setTextColor(0);
    display.setCursor(26, 28);
    display.println("Run PCR");

    //display.fillRect(21,38,86,25,1);
    display.setTextColor(1);
    display.setCursor(40, 55);
    display.println("Program");
  }
  else
  {
    // display.fillRect(21,10,86,25,1);
    display.setTextColor(1);
    display.setCursor(26, 28);
    display.println("Run PCR");

    display.fillRect(21, 38, 86, 25, 1);
    display.setTextColor(0);
    display.setCursor(40, 55);
    display.println("Program");
  }
  display.setFont();
  display.setTextColor(1);
  display.display();
}

// Draw display when a cycling program is running
void draw_run_display()
{
  display.clearDisplay();
  display.fillRect(0, 0, 128, 11, 1);
  display.setCursor(16, 2);
  display.setTextColor(0);
  if (counter % 2 == 0)
  {
    display.println("  PCR Running");
  }
  else
  {
    display.println("Press to STOP PCR");
    if (!digitalRead(butPin))
    {
      while (!digitalRead(butPin));
      caseUX = CASE_Done;
      counter = MenuItem;
    }
  }
  display.setTextColor(1);
  sprintf(displayStr, "s:%i/%i, c:%i/%i, b:%i/%i", cycleAt+1, Programs.Program[SelectedProgram].NumCyclingBlocks, cycleRepeatAt+1, Programs.Program[SelectedProgram].Cycle[cycleAt].CycleRepeatNumber, blockAt+1, Programs.Program[SelectedProgram].Cycle[cycleAt].NumBlocks);
  display.setCursor(0, 14);
  display.print(displayStr);
  sprintf(displayStr, "Overall cycle %i of %i", overallCycleAt+1, Programs.Program[SelectedProgram].OverallCycles);
  display.setCursor(0, 23);
  display.print(displayStr);
  
  display.drawLine(0, 34, 128, 34, 1);
  display.setCursor(0, 55 - 18);
  display.print("Set Temp: ");
  display.print(Programs.Program[SelectedProgram].Cycle[cycleAt].Block[blockAt].TargetTemp, 1);
  display.print(" \xA7");
  display.print("C");

  display.setCursor(0, 55 - 9);
  display.print("Block Temp: ");
  display.print(temperature_mean, 1);
  display.print(" \xA7"); display.print("C");
  display.setCursor(0, 55);
  display.print("Time: ");
  display.print(TIMEcontrol);
  display.print(" s");
  display.setCursor(1, 25);
  display.drawLine(x, 10 * (temperature_mean - 25), x, 10 * (temperature_mean - 25), 1);
  display.display();
}

// rotate is called anytime the rotary inputs change state.
void rotate() {
  delayMicroseconds(500) ;
  unsigned char result = rotary.process();
  if (result == DIR_CW) {
    counter++;
    rotaryTurned = true;
    infoOutput[0]='\0';
    sprintf(infoOutput, "counter:%i", counter);
    Serial.print(infoOutput);
    Serial.println();
  } else if (result == DIR_CCW) {
    counter--;
    rotaryTurned = true;
    infoOutput[0]='\0';
    sprintf(infoOutput, "counter:%i", counter);
    Serial.print(infoOutput);
    Serial.println();
  }
}

// Set the heater "prower" setting
int power_heating(float temperature, float power)
{
  float power_return;

#define FA_c 0.00564542
#define FA_d 0.0418254
#define FA_e 0.000019826
#define FA_f 0.003711

  power_return = (FA_d - FA_f * temperature - power) / (FA_e * temperature - FA_c);

  if (power_return > 255) power_return = 255;
  if (power_return < 0) power_return = 0;

  return (int)power_return;

}

// Set the fan speed when the block is too hot
int power_cooling(float temperature, float power)
{
  float power_return;

#define FA_i 0.000072781
#define FA_j 0.00413579
#define FA_k 0.001372876
#define FA_l 0.1204656

  power_return = (FA_l - FA_j * temperature - power) / (FA_i * temperature - FA_k);

  if (power_return > 255) power_return = 255;
  if (power_return < 0) power_return = 0;

  return (int)power_return;
}

// Exucute PID to determine magnitude of feedback response for heating or fan operation
void runPID()
{
  TEMPcontrol = PIDp * TEMPdif + PIDd * TEMPd + (int)PIDIntegration * PIDi * TEMPi;
  setHeater(temperature_mean, TEMPcontrol);
}

// Set heater or fan according to output of PID algorithm
void setHeater(float temperature, float power)
{
  if (power == 0) {
    pinMode(fanPin, OUTPUT);
    pinMode(heaterPin, OUTPUT);
    digitalWrite(fanPin, LOW);
    digitalWrite(heaterPin, LOW);
  }
  else
  {
    if (TEMPcontrol > 0)
    {
      heatPower = power_heating(temperature, power);
      analogWrite(fanPin, 0);
      analogWrite(heaterPin, heatPower);
    }
    else
    {
      heatPower = power_cooling(temperature, power);
      analogWrite(fanPin, heatPower);
      analogWrite(heaterPin, 0);
    }
  } // else
}
