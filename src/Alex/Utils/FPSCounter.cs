﻿using System;
using System.Diagnostics;

namespace Alex.Utils
{
	public class FpsMonitor
	{
		public float    Value            { get; private set; }
		public TimeSpan AverageFrameTime { get; private set; }
		public TimeSpan LastFrameTime    { get; private set; }

		public           TimeSpan  Sample           { get; set; }
		private readonly Stopwatch _sw;
		private          int       _frames;

		private Stopwatch _frameSw;
		public FpsMonitor()
		{
			this.Sample = TimeSpan.FromSeconds(1);
			this.Value = 0;
			this._frames = 0;
			this._sw = Stopwatch.StartNew();
			this._frameSw = Stopwatch.StartNew();
		}

		public void Update()
		{
            this._frames++;
            if (_sw.Elapsed > Sample)
			{
				this.Value = (float)(_frames / _sw.Elapsed.TotalSeconds);
				this.AverageFrameTime = _sw.Elapsed / _frames;
				this._sw.Reset();
				this._sw.Start();
				this._frames = 0;
			}

            LastFrameTime = _frameSw.Elapsed;
            _frameSw.Restart();
		}
	}
}
