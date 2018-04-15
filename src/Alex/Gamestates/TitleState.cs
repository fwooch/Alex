﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Alex.API.Gui;
using Alex.API.Gui.Elements;
using Alex.API.Gui.Elements.Controls;
using Alex.API.Gui.Rendering;
using Alex.API.Utils;
using Alex.Gamestates.Gui;
using Alex.GameStates.Gui.MainMenu;
using Alex.Graphics;
using Alex.Graphics.Models;
using Alex.Worlds;
using Alex.Worlds.Generators;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;

namespace Alex.Gamestates
{
	public class TitleState : GameState
	{
		private GuiDebugInfo _debugInfo;

		private GuiTextElement _splashText;

		private GuiPanoramaSkyBox _backgroundSkyBox;

		public TitleState(Alex alex, ContentManager content) : base(alex)
		{
			_backgroundSkyBox = new GuiPanoramaSkyBox(alex, alex.GraphicsDevice, content);

			Gui = new GuiScreen(Alex)
			{
				//DefaultBackgroundTexture = GuiTextures.OptionsBackground
			};
			var stackMenu = new GuiStackMenu()
			{
				LayoutOffsetX = 25,
				Width = 125,
				VerticalAlignment = VerticalAlignment.Center,

				VerticalContentAlignment = VerticalAlignment.Top,
				HorizontalContentAlignment = HorizontalAlignment.Stretch
			};

			stackMenu.AddMenuItem("Multiplayer", () =>
			{
				//TODO: Switch to multiplayer serverlist (maybe choose PE or Java?)
				Alex.ConnectToServer();
			});

			stackMenu.AddMenuItem("Multiplayer Servers", () =>
			{
				Alex.GameStateManager.SetActiveState<MultiplayerServerSelectionState>();
			});

			stackMenu.AddMenuItem("Debug Blockstates", DebugWorldButtonActivated);
			stackMenu.AddMenuItem("Debug Flatland", DebugFlatland);
			stackMenu.AddMenuItem("Debug Anvil", DebugAnvil);

			stackMenu.AddMenuItem("Options", () => { Alex.GameStateManager.SetActiveState("options"); });
			stackMenu.AddMenuItem("Exit Game", () => { Alex.Exit(); });

			Gui.AddChild(stackMenu);

			Gui.AddChild(new GuiImage(GuiTextures.AlexLogo)
			{
				LayoutOffsetX = 175,
				LayoutOffsetY = 25
			});
			Gui.AddChild( _splashText = new GuiTextElement(false)
			{
				TextColor = TextColor.Yellow,
				Rotation = 145f,
				//RotationOrigin = Vector2.Zero,
				
				X = 240,
				Y = 25,

				Text = "Who liek minecwaf?!"
			});

			_debugInfo = new GuiDebugInfo(alex);
			_debugInfo.AddDebugRight(() => $"Cursor Position: {alex.InputManager.CursorInputListener.GetCursorPosition()} / {alex.GuiManager.FocusManager.CursorPosition}");
			_debugInfo.AddDebugRight(() => $"Cursor Delta: {alex.InputManager.CursorInputListener.GetCursorPositionDelta()}");
			_debugInfo.AddDebugRight(() => $"Splash Text Scale: {_splashText.Scale:F3}");

		}

		protected override void OnLoad(RenderArgs args)
		{
			//var logo = new UiElement()
			//{
			//	ClassName = "TitleScreenLogo",
			//};
			//Gui.AddChild(logo);

			//SynchronizationContext.Current.Send((o) => _backgroundSkyBox.Load(Alex.GuiRenderer), null);

			Alex.IsMouseVisible = true;
		}

		private float _rotation;

		protected override void OnUpdate(GameTime gameTime)
		{
			_backgroundSkyBox.Update(gameTime);

			_rotation += (float)gameTime.ElapsedGameTime.TotalMilliseconds / (1000.0f / 20.0f);

			_splashText.Scale = 0.75f + (float)Math.Abs(Math.Sin(MathHelper.ToRadians(_rotation * 10.0f))) * 0.75f;

			base.OnUpdate(gameTime);
		}

		protected override void OnDraw3D(RenderArgs args)
		{
			if (!_backgroundSkyBox.Loaded)
			{
				_backgroundSkyBox.Load(Alex.GuiRenderer);
			}

			_backgroundSkyBox.Draw(args);

			base.OnDraw3D(args);
		}

		protected override void OnShow()
		{
			base.OnShow();
			Alex.GuiManager.AddScreen(_debugInfo);
		}

		protected override void OnHide()
		{
			Alex.GuiManager.RemoveScreen(_debugInfo);
			base.OnHide();
		}

		private void Debug(IWorldGenerator generator)
		{
			Alex.IsMultiplayer = false;

			Alex.IsMouseVisible = false;

			generator.Initialize();
			var debugProvider = new SPWorldProvider(Alex, generator);
			Alex.LoadWorld(debugProvider, debugProvider.Network);
		}

		private void DebugFlatland()
		{
			Debug(new FlatlandGenerator());
		}

		private void DebugAnvil()
		{
			Debug(new AnvilWorldProvider(Alex.GameSettings.Anvil)
			{
				MissingChunkProvider = new VoidWorldGenerator()
			});
		}

		private void DebugWorldButtonActivated()
		{
			Debug(new DebugWorldGenerator());
		}
	}
}
