﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ECore.DeviceMemories
{
    public enum REG
    {
		STROBE_UPDATE = 0,
		SPI_ADDRESS = 1,
		SPI_WRITE_VALUE = 2,
		TRIGGERLEVEL = 3,
		TRIGGERHOLDOFF_B0 = 4,
		TRIGGERHOLDOFF_B1 = 5,
		SAMPLECLOCKDIVIDER_B0 = 6,
		SAMPLECLOCKDIVIDER_B1 = 7,
		CHA_YOFFSET_VOLTAGE = 8,
		CHB_YOFFSET_VOLTAGE = 9,
		DIVIDER_MULTIPLIER = 10,
		DIGITAL_OUT = 11,
		TRIGGER_PWM = 12,
		VIEW_DECIMATION = 13,
		VIEW_OFFSET = 14,
		VIEW_ACQUISITIONS = 15,
		VIEW_BURSTS = 16,
    }

    public enum STR
    {
		GLOBAL_NRESET = 0,
		INIT_SPI_TRANSFER = 1,
		AWG_ENABLE = 2,
		LA_ENABLE = 3,
		SCOPE_ENABLE = 4,
		SCOPE_UPDATE = 5,
		FORCE_TRIGGER = 6,
		FREE_RUNNING = 7,
		ACQ_SINGLE = 8,
		ACQ_START = 9,
		ACQ_STOP = 10,
		TRIGGER_CHB = 11,
		TRIGGER_FALLING = 12,
		CHA_DCCOUPLING = 13,
		CHB_DCCOUPLING = 14,
		ENABLE_ADC = 15,
		OVERFLOW_DETECT = 16,
		ENABLE_NEG = 17,
		POWER_DIGITAL_IO = 18,
		ENABLE_RAM = 19,
		DEBUG_PIC = 20,
		DEBUG_RAM = 21,
		DOUT_3V_5V = 22,
		EN_OPAMP_B = 23,
    }

    public enum ROM
    {
		FW_MSB = 0,
		FW_LSB = 1,
		FW_BUILD = 2,
		SPI_RECEIVED_VALUE = 3,
		SCOPE_STATE = 4,
		STROBES = 5,
    }

}
