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
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using ORTS.Common;
using System;

namespace Orts.Viewer3D.Popups
{
    public class TrainForcesWindow : Window
    {
        const float HighCouplerStrengthN = 2.2e6f; // 500 klbf
        const float ImpossiblyHighForce = 9.999e8f;

        Train PlayerTrain;
        int LastPlayerTrainCars;
        bool LastPlayerLocomotiveFlippedState;

        float MaxCouplerStrengthN = 0.0f;
        float MinCouplerStrengthN = ImpossiblyHighForce;

        Image[] CouplerForceBarGraph;
        static Texture2D ForceBarTextures;

        Label MaxForceLabelValue;

        public TrainForcesWindow(WindowManager owner)
            : base(owner, Window.DecorationSize.X + owner.TextFontDefault.Height * 50, Window.DecorationSize.Y + owner.TextFontDefault.Height * 6, Viewer.Catalog.GetString("Train Forces"))
        {
        }

        protected internal override void Initialize()
        {
            base.Initialize();
            if (ForceBarTextures == null)
            {
                ForceBarTextures = SharedTextureManager.Get(Owner.Viewer.RenderProcess.GraphicsDevice, System.IO.Path.Combine(Owner.Viewer.ContentPath, "BarGraph.png"));
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
                CouplerForceBarGraph = new Image[PlayerTrain.Cars.Count];

                int carPosition = 0;
                foreach (var car in PlayerTrain.Cars)
                {
                    scrollbox.Add(CouplerForceBarGraph[carPosition] = new Image(6, 40));
                    CouplerForceBarGraph[carPosition].Texture = ForceBarTextures;
                    UpdateCouplerImage(car, carPosition);
                    carPosition++;
                }

                var textbox = vbox.AddLayoutHorizontalLineOfText();
                textbox.Add(new Label(7 * textHeight, textHeight, Viewer.Catalog.GetString("Max Force:"), LabelAlignment.Right));
                textbox.Add(MaxForceLabelValue = new Label(5 * textHeight, textHeight, FormatStrings.FormatLargeForce(0f, false), LabelAlignment.Right));
                textbox.Add(new Label(9 * textHeight, textHeight, Viewer.Catalog.GetString("Coupler Strength:"), LabelAlignment.Right));
                textbox.Add(new Label(5 * textHeight, textHeight, FormatStrings.FormatLargeForce(MinCouplerStrengthN, false), LabelAlignment.Right));
                textbox.Add(new Label(1 * textHeight, textHeight, " - ", LabelAlignment.Center));
                textbox.Add(new Label(5 * textHeight, textHeight, FormatStrings.FormatLargeForce(MaxCouplerStrengthN, false), LabelAlignment.Left));
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
                var absMaxForceN = 0.0f; var forceSign = 1.0f;

                int carPosition = 0;
                foreach (var car in PlayerTrain.Cars)
                {
                    UpdateCouplerImage(car, carPosition);

                    var forceN = car.CouplerForceU; var absForceN = Math.Abs(forceN);
                    if (absForceN > absMaxForceN) { absMaxForceN = absForceN; forceSign = forceN > 0 ? 1.0f : -1.0f; }

                    carPosition++;
                }

                if (MaxForceLabelValue != null) { MaxForceLabelValue.Text = FormatStrings.FormatLargeForce(absMaxForceN * forceSign, false); }
            }
        }

        protected void SetConsistProperties(Train theTrain)
        {
            float lengthM = 0.0f;
            float massKg = 0.0f;
            float powerW = 0.0f;
            float maxCouplerBreakN = 0.0f;
            float minCouplerBreakN = ImpossiblyHighForce;

            foreach (var car in theTrain.Cars)
            {
                lengthM += car.CarLengthM;
                massKg += car.MassKG;
                if (car is MSTSWagon wag)
                {
                    var couplerBreakForceN = wag.GetCouplerBreak2N() > 1.0f ? wag.GetCouplerBreak2N() : wag.GetCouplerBreak1N();
                    if (couplerBreakForceN < minCouplerBreakN) { minCouplerBreakN = couplerBreakForceN; }
                    if (couplerBreakForceN > maxCouplerBreakN) { maxCouplerBreakN = couplerBreakForceN; }
                }
                if (car is MSTSLocomotive eng) { powerW += eng.MaxPowerW; }
            }
            MaxCouplerStrengthN = Math.Min( maxCouplerBreakN, HighCouplerStrengthN);
            MinCouplerStrengthN = Math.Min(minCouplerBreakN, maxCouplerBreakN);
        }

        protected void UpdateCouplerImage(TrainCar car, int carPosition)
        {
            var idx = CalcBarIndex(car.SmoothedCouplerForceUN);
            if (car.WagonType == TrainCar.WagonTypes.Engine) { CouplerForceBarGraph[carPosition].Source = new Rectangle(1 + idx * 6, 0, 6, 40); }
            else { CouplerForceBarGraph[carPosition].Source = new Rectangle(1 + idx * 6, 40, 6, 40); }
        }

        protected int CalcBarIndex( float forceN)
        {
            // the image has 19 icons, 0 is max push, 9 is neutral, 18 is max pull
            var idx = 9;
            var absForceN = Math.Abs(forceN);
            if (absForceN > 1000f && MinCouplerStrengthN > 1000f)
            {
                // TODO: for push force, may need to scale differently (how?); containers derail at 300 klbf
                var relForce = absForceN / MinCouplerStrengthN;
                var expForce = Math.Pow(9, relForce);
                idx = (int)Math.Floor(expForce);
                idx = (forceN > 0f) ? idx * -1 + 9: idx + 9; // positive force is push
                if (idx < 0) { idx = 0; } else if (idx > 18) { idx = 18; }
            }
            return idx;
        }
    }
}
