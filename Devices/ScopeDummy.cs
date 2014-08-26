﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ECore.DataSources;
using Common;

namespace ECore.Devices {
	public enum WaveSource {
		FILE,
		GENERATOR
	}

    public struct ScopeDummyChannelConfig
    {
        public WaveForm waveform;
        public double amplitude;
        public Coupling coupling;
        public double dcOffset;
        public double frequency;
        public double phase;
        public double noise;
    }

	public partial class ScopeDummy : EDevice, IScope {
		private DataSources.DataSourceScope dataSourceScope;

		public DataSources.DataSourceScope DataSourceScope { get { return dataSourceScope; } }

		private DateTime timeOrigin;
		//Wave settings
		private WaveSource waveSource = WaveSource.GENERATOR;
		private TriggerMode triggerMode = TriggerMode.ANALOG;
		//Noise mean voltage
		private int usbLatency = 40;
		//milliseconds of latency to simulate USB request delay
        private Dictionary<AnalogChannel, float> yOffset = new Dictionary<AnalogChannel, float>() {
            { AnalogChannel.ChA, 0f},
            { AnalogChannel.ChB, 0f}
        };
		//Scope variables
		private const int waveLength = 3 * outputWaveLength;
		private double samplePeriodMinimum = 10e-9;
		//ns --> sampleFreq of 100MHz by default
		private double SamplePeriod { get { return samplePeriodMinimum * decimation; } }

        public const uint channels = 2;
        private Dictionary<AnalogChannel, ScopeDummyChannelConfig> _channelConfig = new Dictionary<AnalogChannel, ScopeDummyChannelConfig>();
        public Dictionary<AnalogChannel, ScopeDummyChannelConfig> ChannelConfig { get { return _channelConfig; } }

		private const int outputWaveLength = 2048;
		private float triggerLevel = 0;
		private double triggerHoldoff = 0;
		private AnalogChannel triggerChannel = AnalogChannel.ChA;
		private static uint triggerWidth = 10;

        private struct DigitalTrigger {
            public byte triggerCondition;
            public byte triggerMask;
            public byte preTriggerCondition;
            public byte preTriggerMask;
        }
        private DigitalTrigger digitalTrigger;

		private uint decimation = 1;
		private TriggerDirection triggerDirection = TriggerDirection.FALLING;
        private AcquisitionMode acquisitionMode = AcquisitionMode.NORMAL;
        private bool acquisitionRunning = false;
		//Hack
		bool regenerate = true;
		DataPackageScope p;
        private int maxAttempts = 10;

		#region constructor / initializer

		public ScopeDummy (ScopeConnectHandler handler)
            : base ()
		{
            foreach(AnalogChannel ch in AnalogChannel.listPhysical)
            {
                _channelConfig.Add(ch, new ScopeDummyChannelConfig()
                {
                    amplitude = 2.0,
                    noise = 0.1,
                    coupling = Coupling.DC,
                    dcOffset = 0.0,
                    frequency = 135e3,
                    phase = 0,
                    waveform = WaveForm.TRIANGLE
                });
            }

            timeOrigin = DateTime.Now;

			dataSourceScope = new DataSources.DataSourceScope (this);
			if (handler != null)
				handler (this, true);
		}

        public void CommitSettings()
        {
            //Nothign to do here, all settings are updated immediately;
        }

		#endregion

		#region real scope settings

		private void validateDecimation (uint decimation)
		{
			if (decimation < 1)
				throw new ValidationException ("Decimation must be larger than 0");
		}

        public void SetAcquisitionMode(AcquisitionMode mode)
        {
            this.acquisitionMode = mode;
        }
        public void SetAcquisitionRunning(bool running)
        {
            this.acquisitionRunning = running;
        }
        public bool GetAcquisitionRunning()
        {
            return this.acquisitionRunning;
        }

		public void SetTriggerHoldOff (double holdoff)
		{
			this.triggerHoldoff = holdoff;
		}

		public void SetTriggerAnalog (float voltage)
		{
			this.triggerLevel = voltage;
		}

        public void SetVerticalRange(AnalogChannel ch, float minimum, float maximum)
        {
        }

        public void SetYOffset(AnalogChannel ch, float yOffset)
		{
            if (!ch.Physical) return;
			this.yOffset [ch] = yOffset;
		}

        public void SetTriggerChannel(AnalogChannel ch)
		{
            if (!ch.Physical) return;
			this.triggerChannel = ch;
		}

		public void SetTriggerDirection (TriggerDirection direction)
		{
			this.triggerDirection = direction;
		}
        public void SetForceTrigger()
        {
            throw new NotImplementedException();
        }

		public void SetTriggerMode (TriggerMode mode)
		{
			this.triggerMode = mode;
		}

        public void SetTriggerWidth(uint width)
        {
            throw new NotImplementedException();
        }
        public uint GetTriggerWidth()
        {
            throw new NotImplementedException();
        }
        public void SetTriggerThreshold(uint threshold)
        {
            throw new NotImplementedException();
        }

        public uint GetTriggerThreshold()
        {
            throw new NotImplementedException();
        }

        public void SetTriggerDigital(Dictionary<DigitalChannel, DigitalTriggerValue> condition)
		{
            digitalTrigger.triggerCondition = 0x0;
            digitalTrigger.triggerMask = 0xFF;
            digitalTrigger.preTriggerCondition = 0x0;
            digitalTrigger.preTriggerMask = 0xFF;
            foreach (KeyValuePair<DigitalChannel, DigitalTriggerValue> kvp in condition)
            {
                int bit = kvp.Key.Value;
                DigitalTriggerValue value = kvp.Value;
                if (value == DigitalTriggerValue.X)
                {
                    Utils.ClearBit(ref digitalTrigger.triggerMask, bit);
                    Utils.ClearBit(ref digitalTrigger.preTriggerMask, bit);
                }
                if (value == DigitalTriggerValue.I)
                {
                    Utils.ClearBit(ref digitalTrigger.preTriggerMask, bit);
                    Utils.SetBit(ref digitalTrigger.triggerCondition, bit);
                }
                if (value == DigitalTriggerValue.O)
                {
                    Utils.ClearBit(ref digitalTrigger.preTriggerMask, bit);
                    Utils.ClearBit(ref digitalTrigger.triggerCondition, bit);
                }
                if (value == DigitalTriggerValue.R)
                {
                    Utils.ClearBit(ref digitalTrigger.preTriggerCondition, bit);
                    Utils.SetBit(ref digitalTrigger.triggerCondition, bit);
                }
                if (value == DigitalTriggerValue.F)
                {
                    Utils.SetBit(ref digitalTrigger.preTriggerCondition, bit);
                    Utils.ClearBit(ref digitalTrigger.triggerCondition, bit);
                }
            }
		}

		public void SetTimeRange (double timeRange)
		{
			decimation = 1;
			while (timeRange > decimation * GetDefaultTimeRange ())
				decimation++;
		}

		public void SetCoupling (AnalogChannel ch, Coupling coupling)
		{
            if (!ch.Physical) return;
            ScopeDummyChannelConfig config = _channelConfig[ch];
            config.coupling = coupling;
            _channelConfig[ch] = config;
		}

		public Coupling GetCoupling (AnalogChannel ch)
		{
            if (!ch.Physical) return Coupling.AC;
            return _channelConfig[ch].coupling;
		}

		public double GetDefaultTimeRange ()
		{ 
			return outputWaveLength * samplePeriodMinimum; 
		}

		#endregion

		#region dummy scope settings

        public void SetDummyWaveSource(WaveSource source)
        {
            this.waveSource = source;
        }

		public void SetDummyWaveAmplitude (AnalogChannel channel, double amplitude)
		{
            if (!channel.Physical) return;
            ScopeDummyChannelConfig config = _channelConfig[channel];
            config.amplitude = amplitude;
            _channelConfig[channel] = config;
		}

        public void SetDummyWaveFrequency(AnalogChannel channel, double frequency)
		{
            if (!channel.Physical) return;
            ScopeDummyChannelConfig config = _channelConfig[channel];
            config.frequency = frequency;
            _channelConfig[channel] = config;
		}

        public void SetDummyWavePhase(AnalogChannel channel, double phase)
        {
            if (!channel.Physical) return;
            ScopeDummyChannelConfig config = _channelConfig[channel];
            config.phase = phase;
            _channelConfig[channel] = config;

        }

        public void SetDummyWaveForm(AnalogChannel channel, WaveForm w)
		{
            if (!channel.Physical) return;
            ScopeDummyChannelConfig config = _channelConfig[channel];
            config.waveform = w;
            _channelConfig[channel] = config;

		}

        public void SetDummyWaveDcOffset(AnalogChannel channel, double dcOffset)
        {
            if (!channel.Physical) return;
            ScopeDummyChannelConfig config = _channelConfig[channel];
            config.dcOffset = dcOffset;
            _channelConfig[channel] = config;

        }

        public void SetNoiseAmplitude(AnalogChannel channel, double noiseAmplitude)
		{
            if (!channel.Physical) return;
            ScopeDummyChannelConfig config = _channelConfig[channel];
            config.noise = noiseAmplitude;
            _channelConfig[channel] = config;

		}

		#endregion

		private static bool TriggerAnalog (AcquisitionMode acqMode, float [] wave, TriggerDirection direction, int holdoff, float level, float noise, uint outputWaveLength, out int triggerIndex)
		{
			//Hold off:
			// - if positive, start looking for trigger at that index, so we are sure to have that many samples before the trigger
			// - if negative, start looking at index 0
			triggerIndex = 0;
			for (int i = Math.Max (0, holdoff); i < wave.Length - triggerWidth - outputWaveLength; i++) {
				float invertor = (direction == TriggerDirection.RISING) ? 1f : -1f;
				if (invertor * wave [i] < invertor * level - noise && invertor * wave [i + triggerWidth] + noise > invertor * level) {
					triggerIndex = (int) (i + triggerWidth / 2);
					return true;
				}
			}
            if (acqMode == AcquisitionMode.AUTO)
            {
                triggerIndex = holdoff;
                return true;
            }
            else
			    return false;
		}

        private static bool TriggerDigital(byte[] wave, int holdoff, DigitalTrigger trigger, uint outputWaveLength, out int triggerIndex)
		{
			//Hold off:
			// - if positive, start looking for trigger at that index, so we are sure to have that many samples before the trigger
			// - if negative, start looking at index 0
			triggerIndex = 0;
			for (int i = Math.Max (1, holdoff); i < wave.Length - outputWaveLength; i++) {
				if (
                    (wave[i] & trigger.triggerMask) == trigger.triggerCondition 
                    &&
                    (wave[i - 1] & trigger.preTriggerMask) == trigger.preTriggerCondition 
                    ) {
                    triggerIndex = i;
					return true;
				}
			}
			return false;
		}

		public DataPackageScope GetScopeData ()
		{
			//Sleep to simulate USB delay
			System.Threading.Thread.Sleep (usbLatency);
			float [][] outputAnalog = null;
			byte [] outputDigital = null;
			int triggerIndex = 0;
			int triggerHoldoffInSamples = 0;

            while (!acquisitionRunning)
                System.Threading.Thread.Sleep(10);

            TimeSpan timeOffset = DateTime.Now - timeOrigin;
			if (regenerate) {
				if (waveSource == WaveSource.GENERATOR) {
                    byte[] waveDigital = null;
                    float[][] waveAnalog = null;
                    bool triggerDetected = false;
                    
                    for (int k = 0; k < maxAttempts; k++)
                    {
                        //Generate analog wave
                        waveAnalog = new float[AnalogChannel.listPhysical.Count][];
                        timeOffset = DateTime.Now - timeOrigin;
                        foreach(AnalogChannel channel in AnalogChannel.listPhysical)
                        {
                            int i = channel.Value;
                            waveAnalog[i] = ScopeDummy.GenerateWave(waveLength,
                                SamplePeriod,
                                timeOffset.TotalSeconds,
                                _channelConfig[channel]);
                            if (_channelConfig[channel].coupling == Coupling.AC)
                                ScopeDummy.RemoveDcComponent(ref waveAnalog[i], _channelConfig[channel].frequency, SamplePeriod);
                            else
                                ScopeDummy.AddDcComponent(ref waveAnalog[i], (float)_channelConfig[channel].dcOffset);
                            ScopeDummy.AddNoise(waveAnalog[i], _channelConfig[channel].noise);
                        }

                        //Generate some bullshit digital wave
                        waveDigital = new byte[waveLength];
                        byte value = (byte)(timeOffset.Milliseconds);
                        for (int i = 0; i < waveDigital.Length; i++)
                        {
                            waveDigital[i] = value;
                            if (i % 10 == 0)
                                value++;
                        }

                        //Trigger detection
                        triggerHoldoffInSamples = (int)(triggerHoldoff / SamplePeriod);


                        switch (triggerMode)
                        {
                            case TriggerMode.ANALOG:
                                triggerDetected = ScopeDummy.TriggerAnalog(acquisitionMode, waveAnalog[triggerChannel.Value], triggerDirection,
                                    triggerHoldoffInSamples, triggerLevel, (float)_channelConfig[triggerChannel].noise,
                                    outputWaveLength, out triggerIndex);
                                break;
                            case TriggerMode.DIGITAL:
                                triggerDetected = ScopeDummy.TriggerDigital(waveDigital, triggerHoldoffInSamples, digitalTrigger, outputWaveLength, out triggerIndex);
                                if (!triggerDetected && acquisitionMode == AcquisitionMode.AUTO)
                                {
                                    triggerDetected = true;
                                    triggerIndex = 0;
                                }
                                break;
                        }
                        if (triggerDetected)
                            break;
                    }
                    if (!triggerDetected)
                        return null;

					outputAnalog = new float[channels][];
					for (int i = 0; i < channels; i++) {
						outputAnalog [i] = ScopeDummy.CropWave (outputWaveLength, waveAnalog [i], triggerIndex, triggerHoldoffInSamples);
					}
					outputDigital = ScopeDummy.CropWave (outputWaveLength, waveDigital, triggerIndex, triggerHoldoffInSamples);
				} else if (waveSource == WaveSource.FILE) {
					if (!GetWaveFromFile (acquisitionMode, triggerMode, triggerHoldoff, triggerChannel, triggerDirection, triggerLevel, decimation, SamplePeriod, ref outputAnalog))
						return null;
					foreach(AnalogChannel ch in AnalogChannel.listPhysical)
                        ScopeDummy.AddNoise(outputAnalog[ch.Value], _channelConfig[ch].noise);
					triggerHoldoffInSamples = (int) (triggerHoldoff / SamplePeriod);
				}
                double firstSampleTime = (timeOffset.TotalMilliseconds / 1.0e3) + (triggerIndex - triggerHoldoffInSamples) * SamplePeriod;
                UInt64 firstSampleTimeNs = (UInt64)(firstSampleTime * 1e9);
				p = new DataPackageScope (SamplePeriod, triggerHoldoffInSamples, outputWaveLength, firstSampleTimeNs);
				p.SetData (AnalogChannel.ChA, outputAnalog [0]);
                p.SetData(AnalogChannel.ChB, outputAnalog[1]);

				p.SetDataDigital (outputDigital);
			}
#if __IOS__
			regenerate = true;
#endif

            if (acquisitionMode == AcquisitionMode.SINGLE)
                acquisitionRunning = false;

			return p;
		}

        private static void AddDcComponent(ref float[] p, float offset)
        {
            if (offset == 0f) 
                return;
            p = p.AsParallel().Select(x => x + offset).ToArray();
        }

        private static void RemoveDcComponent(ref float[] p, double frequency, double samplePeriod)
        {
            int periodLength = (int)Math.Round(1.0 / (frequency * samplePeriod));
            float mean = p.Take(periodLength).Average();
            if (mean == 0f) 
                return;
            p = p.AsParallel().Select(x => x - mean).ToArray();
        }
	}
}
