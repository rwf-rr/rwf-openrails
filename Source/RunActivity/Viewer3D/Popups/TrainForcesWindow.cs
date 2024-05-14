// COPYRIGHT 2010, 2011, 2012, 2013, 2014, 2015 by the Open Rails project.
// 
// This file is part of Open Rails.
// 
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.

// This file is the responsibility of the 3D & Environment Team. 

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Orts.Formats.Msts;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using ORTS.Common;
using SharpDX.Direct2D1;
using SharpDX.MediaFoundation;
using System;
using System.Linq;

namespace Orts.Viewer3D.Popups
{
    public class TrainForcesWindow : Window
    {
        const float ImpossibleHighForce = 9.999e8f;

        Train PlayerTrain;
        int LastPlayerTrainCars;
        bool LastPlayerLocomotiveFlippedState;

        float TrainLengthM = 0.0f;
        float TrainMassKg = 0.0f;
        float TrainPowerW = 0.0f;
        float MinCouplerStrengthN = ImpossibleHighForce;

        Image[] RearCouplerBar;
        static Texture2D BarTextures;
        static Random Rnd = new Random();  // temporart, for testing

        public TrainForcesWindow(WindowManager owner)
            : base(owner, Window.DecorationSize.X + owner.TextFontDefault.Height * 80, Window.DecorationSize.Y + owner.TextFontDefault.Height * 6, Viewer.Catalog.GetString("Train Forces"))
        {
        }

        protected internal override void Initialize()
        {
            base.Initialize();
            if (BarTextures == null)
            {
                BarTextures = SharedTextureManager.Get(Owner.Viewer.RenderProcess.GraphicsDevice, System.IO.Path.Combine(Owner.Viewer.ContentPath, "BarGraph.png"));
            }
        }

        protected override ControlLayout Layout(ControlLayout layout)
        {
            var textHeight = Owner.TextFontDefault.Height;

            var vbox = base.Layout(layout).AddLayoutVertical();
            var scrollbox = vbox.AddLayoutScrollboxHorizontal(vbox.RemainingHeight - textHeight);
            if (PlayerTrain != null)
            {
                SetConsistProperties(PlayerTrain);
                RearCouplerBar = new Image[PlayerTrain.Cars.Count];

                int carPosition = 0;
                foreach (var car in PlayerTrain.Cars)
                {
                    scrollbox.Add(RearCouplerBar[carPosition] = new Image(6, 40));
                    RearCouplerBar[carPosition].Texture = BarTextures;
                    UpdateCouplerImage(car, carPosition);
                    carPosition++;
                }
                var textbox = vbox.AddLayoutHorizontalLineOfText();
                textbox.Add(new Label(5 * textHeight, textHeight, Viewer.Catalog.GetString("Length:"), LabelAlignment.Right));
                textbox.Add(new Label(5 * textHeight, textHeight, FormatStrings.FormatShortDistanceDisplay( TrainLengthM, false), LabelAlignment.Left));
                textbox.Add(new Label(5 * textHeight, textHeight, Viewer.Catalog.GetString("Weight:"), LabelAlignment.Right));
                textbox.Add(new Label(5 * textHeight, textHeight, FormatStrings.FormatLargeMass(TrainMassKg, false, false), LabelAlignment.Left));
                textbox.Add(new Label(5 * textHeight, textHeight, Viewer.Catalog.GetString("Power:"), LabelAlignment.Right));
                textbox.Add(new Label(5 * textHeight, textHeight, FormatStrings.FormatPower(TrainPowerW, false, false, false), LabelAlignment.Left));
                textbox.Add(new Label(6 * textHeight, textHeight, Viewer.Catalog.GetString("Coupler Strength:"), LabelAlignment.Right));
                textbox.Add(new Label(6 * textHeight, textHeight, FormatStrings.FormatForce(MinCouplerStrengthN, false), LabelAlignment.Left));
            }
            return vbox;
        }

        public override void PrepareFrame(ElapsedTime elapsedTime, bool updateFull)
        {
            base.PrepareFrame(elapsedTime, updateFull);

            if (updateFull)
            {
                if (PlayerTrain != Owner.Viewer.PlayerTrain || Owner.Viewer.PlayerTrain.Cars.Count != LastPlayerTrainCars || (Owner.Viewer.PlayerLocomotive != null &&
                    LastPlayerLocomotiveFlippedState != Owner.Viewer.PlayerLocomotive.Flipped))
                {
                    PlayerTrain = Owner.Viewer.PlayerTrain;
                    LastPlayerTrainCars = Owner.Viewer.PlayerTrain.Cars.Count;
                    if (Owner.Viewer.PlayerLocomotive != null) LastPlayerLocomotiveFlippedState = Owner.Viewer.PlayerLocomotive.Flipped;
                    Layout();
                }
            }
            else if (PlayerTrain != null)
            {
                int carPosition = 0;
                foreach (var car in PlayerTrain.Cars)
                {
                    UpdateCouplerImage(car, carPosition);
                    carPosition++;
                }
            }
        }

        protected void SetConsistProperties(Train theTrain)
        {
            float lengthM = 0.0f;
            float massKg = 0.0f;
            float powerW = 0.0f;
            float minCouplerBreakN = ImpossibleHighForce;

            foreach (var car in theTrain.Cars)
            {
                lengthM += car.CarLengthM;
                massKg += car.MassKG;
                if (car is MSTSWagon wag)
                {
                    var couplerBreakForceN = wag.GetCouplerBreak2N() > 1.0f ? wag.GetCouplerBreak2N() : wag.GetCouplerBreak1N();
                    if (couplerBreakForceN < minCouplerBreakN) { minCouplerBreakN = couplerBreakForceN; }

                    // losely based on TrainCar.UpdateTrainRerailmentRisk
                    var numWheels = (wag.LocoNumDrvAxles + wag.GetWagonNumAxles()) * 2;
                    var derailForceN = (wag.MassKG / numWheels) * wag.GetGravitationalAccelerationMpS2();
                }
                if (car is MSTSLocomotive eng) { powerW += eng.MaxPowerW; }
            }
            TrainLengthM = lengthM;
            TrainMassKg = massKg;
            TrainPowerW = powerW;
            MinCouplerStrengthN = minCouplerBreakN;
        }

        protected void UpdateCouplerImage(TrainCar car, int carPosition)
        {
            var idx = CalcBarIndex(car.SmoothedCouplerForceUN, car.Flipped);
            if (car.WagonType == TrainCar.WagonTypes.Engine) { RearCouplerBar[carPosition].Source = new Rectangle(1 + idx * 6, 0, 6, 40); }
            else { RearCouplerBar[carPosition].Source = new Rectangle(1 + idx * 6, 40, 6, 40); }
        }

        protected int CalcBarIndex( float forceN, bool flipped)
        {
            var idx = 9;
            var absForceN = Math.Abs(forceN);
            if (absForceN > 1000f && MinCouplerStrengthN > 1000f)
            {
                var relForce = absForceN / MinCouplerStrengthN * 9f + 1f;
                var log10Force = Math.Log10(relForce);
                //if ((forceN < 0f) != flipped) { log10Force *= -1; }
                if (forceN > 0f) { log10Force *= -1; }
                idx = (int)(log10Force * 9f) + 9;
                if (idx < 0) { idx = 0; } else if (idx > 18) { idx = 18; }
            }
            return idx;
        }
    }
}
