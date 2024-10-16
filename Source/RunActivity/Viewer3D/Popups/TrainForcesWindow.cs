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

// This is a prorotype to evaluate a train forces popup display. The
// intent is to provide real-time train-handling feeback of the forces
// within the train, particularly for long, heavy freight trains. The
// Forces HUD, display or browser, is hear to read.
// An alternative, better in the long-term, might be an external window
// that provides both in-train and over time feedback, as seen on
// professional training simulators.
// See the discussion in the Elvas tower forum, at:
// https://www.elvastower.com/forums/index.php?/topic/38056-proposal-for-train-forces-popup-display/
//
// Force:
// Shows the pull or push force at each coupling, as a colored bar graph.
// The scale is determined by the weakest coupler in the train. The steps
// are logarithmic, to provide more sensitivity near the breaking point.
//
// Dearail Coefficient:
// Shows the derail coefficient (later force vs vertical force) as a
// colored bar graph. The value may exceed 1.0 (without the train
// derailing. The steps are logarithmic, to provide more sensitivity near
// the derailing point.
//
// Slack:
// Shows the slack, in or out, as a bar graph. Yet to be evaluated.
//
// Break Pipe Pressure:
// Not force related, but useful for the train handling. Shows how
// propagation relates to train forces.

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using ORTS.Common;
using System.Diagnostics;
using System;

namespace Orts.Viewer3D.Popups
{
    public class TrainForcesWindow : Window
    {
        const float HighCouplerStrengthN = 2.2e6f; // 500 klbf
        const float ImpossiblyHighForce = 9.999e8f;

        static Texture2D ForceBarTextures;
        const int BarGraphHight = 40;
        const int HalfBarGraphHight = 20;
        const int BarGraphWidth = 6;

        float SimSeconds = 0f;
        float LastLogSeconds = 0f;
        int PrepareCalls = 0;

        Train PlayerTrain;
        int LastPlayerTrainCars;
        bool LastPlayerLocomotiveFlippedState;

        float MaxCouplerStrengthN = 0.0f;
        float MinCouplerStrengthN = ImpossiblyHighForce;
        float CouplerStrengthScaleN;

        Image[] CouplerForceBarGraph;
        Image[] CouplerImpulseBarGraph;
        Image[] DerailCoeffBarGraph;
        Image[] SlackBarGraph;

        Label MaxForceValueLabel;
        Label MaxImpulseValueLabel; float PreviousMaxImpulseValue = 0f; float PreviousMaxImpulseTime = 0f;
        Label MaxDerailCoeffValueLabel;

        public TrainForcesWindow(WindowManager owner)
            : base(owner, Window.DecorationSize.X + owner.TextFontDefault.Height * 60, Window.DecorationSize.Y + owner.TextFontDefault.Height * 13, Viewer.Catalog.GetString("Train Forces"))
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
            var innerBox = scrollbox.AddLayoutVertical(scrollbox.RemainingHeight);
            var forceBox = innerBox.AddLayoutHorizontal(BarGraphHight + 4);
            forceBox.Add(new Label(0, (BarGraphHight - textHeight) / 2, 5 * textHeight, BarGraphHight, Viewer.Catalog.GetString("Force:")));
            var impulseBox = innerBox.AddLayoutHorizontal(BarGraphHight + 4);
            impulseBox.Add(new Label(0, (BarGraphHight - textHeight) / 2, 5 * textHeight, BarGraphHight, Viewer.Catalog.GetString("Impulse:")));
            var derailCoeffBox = innerBox.AddLayoutHorizontal(HalfBarGraphHight + 4);
            derailCoeffBox.Add(new Label(0, (HalfBarGraphHight - textHeight) / 2, 5 * textHeight, HalfBarGraphHight, Viewer.Catalog.GetString("Derail:")));
            var slackBox = innerBox.AddLayoutHorizontal(BarGraphHight + 4);
            slackBox.Add(new Label(0, (BarGraphHight - textHeight) / 2, 5 * textHeight, BarGraphHight, Viewer.Catalog.GetString("Slack:")));

            if (PlayerTrain != null)
            {
                SetConsistProperties(PlayerTrain);

                CouplerForceBarGraph = new Image[PlayerTrain.Cars.Count];
                CouplerImpulseBarGraph = new Image[PlayerTrain.Cars.Count];
                DerailCoeffBarGraph = new Image[PlayerTrain.Cars.Count];
                SlackBarGraph = new Image[PlayerTrain.Cars.Count];

                int carPosition = 0;
                foreach (var car in PlayerTrain.Cars)
                {
                    forceBox.Add(CouplerForceBarGraph[carPosition] = new Image(BarGraphWidth, BarGraphHight));
                    CouplerForceBarGraph[carPosition].Texture = ForceBarTextures;
                    UpdateCouplerForceImage(car, carPosition);

                    impulseBox.Add(CouplerImpulseBarGraph[carPosition] = new Image(BarGraphWidth, BarGraphHight));
                    CouplerImpulseBarGraph[carPosition].Texture = ForceBarTextures;
                    UpdateCouplerImpulseImage(car, carPosition);

                    derailCoeffBox.Add(DerailCoeffBarGraph[carPosition] = new Image(BarGraphWidth, HalfBarGraphHight));
                    DerailCoeffBarGraph[carPosition].Texture = ForceBarTextures;
                    UpdateDerailCoeffImage(car, carPosition);

                    slackBox.Add(SlackBarGraph[carPosition] = new Image(BarGraphWidth, BarGraphHight));
                    SlackBarGraph[carPosition].Texture = ForceBarTextures;
                    UpdateSlackImage(car, carPosition);

                    carPosition++;
                }

                var textbox = vbox.AddLayoutHorizontalLineOfText();
                textbox.Add(new Label(7 * textHeight, textHeight, Viewer.Catalog.GetString("Max Force:"), LabelAlignment.Right));
                textbox.Add(MaxForceValueLabel = new Label(6 * textHeight, textHeight, FormatStrings.FormatLargeForce(0f, false), LabelAlignment.Right));
                textbox.Add(new Label(7 * textHeight, textHeight, Viewer.Catalog.GetString("Max Impulse:"), LabelAlignment.Right));
                textbox.Add(MaxImpulseValueLabel = new Label(6 * textHeight, textHeight, FormatStrings.FormatLargeForce(0f, false), LabelAlignment.Right));
                textbox.Add(new Label(9 * textHeight, textHeight, Viewer.Catalog.GetString("Max Derail Coeff:"), LabelAlignment.Right));
                textbox.Add(MaxDerailCoeffValueLabel = new Label(5 * textHeight, textHeight, String.Format("{0:F0}%", 0f), LabelAlignment.Right));
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

            //PrepareCalls++;
            //SimSeconds += elapsedTime.RealSeconds;
            //
            //if (PrepareCalls % 60 == 0)
            //{
            //    // Trace.TraceInformation("TrainForcesWindow:PrepareFrame() called for the {0} time; elapsed time is {1:F3}", PrepareCalls, SimSeconds - LastLogSeconds);
            //    LastLogSeconds = SimSeconds;
            //}

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
                var absMaxForceN = 0.0f; var forceSign = 1.0f; var maxForceCarNum = 0;
                var absMaxImpulseN = 0.0f; var impulseSign = 1.0f; var maxImpulseCarNum = 0;
                var maxDerailCoeff = 0.0f;

                int carPosition = 0;
                foreach (var car in PlayerTrain.Cars)
                {
                    UpdateCouplerForceImage(car, carPosition);
                    UpdateCouplerImpulseImage(car, carPosition);
                    UpdateDerailCoeffImage(car, carPosition);
                    UpdateSlackImage(car, carPosition);

                    var forceN = car.CouplerForceU; var absForceN = Math.Abs(forceN);
                    if (absForceN > absMaxForceN) { absMaxForceN = absForceN; forceSign = forceN > 0 ? 1.0f : -1.0f; maxForceCarNum = carPosition + 1; }
                    var impulseN = car.ImpulseCouplerForceUN; var absImpulseN = Math.Abs(impulseN);
                    if (absImpulseN > absMaxImpulseN) { absMaxImpulseN = absImpulseN; impulseSign = impulseN > 0 ? 1.0f : -1.0f; maxImpulseCarNum = carPosition + 1; }
                    if (car.DerailmentCoefficient > maxDerailCoeff) { maxDerailCoeff = car.DerailmentCoefficient; }

                    carPosition++;
                }

                if (MaxForceValueLabel != null) { MaxForceValueLabel.Text = FormatStrings.FormatLargeForce(absMaxForceN * forceSign, false) + string.Format("  ({0})", maxForceCarNum); }
                if (MaxImpulseValueLabel != null)
                {
                    if (absMaxImpulseN > PreviousMaxImpulseValue)
                    {
                        MaxImpulseValueLabel.Text = FormatStrings.FormatLargeForce(absMaxImpulseN * impulseSign, false) + string.Format("  ({0})", maxImpulseCarNum);
                        PreviousMaxImpulseValue = absMaxImpulseN;
                        PreviousMaxImpulseTime = SimSeconds;
                    }
                    else if (absMaxImpulseN < PreviousMaxImpulseValue && SimSeconds > (PreviousMaxImpulseTime + 1f))
                    {
                        MaxImpulseValueLabel.Text = FormatStrings.FormatLargeForce(absMaxImpulseN * impulseSign, false) + string.Format("  ({0})", maxImpulseCarNum);
                        PreviousMaxImpulseValue = absMaxImpulseN;
                        PreviousMaxImpulseTime = SimSeconds;
                    }
                }
                if (MaxDerailCoeffValueLabel != null) { MaxDerailCoeffValueLabel.Text = String.Format("{0:F0}%", maxDerailCoeff * 100f); }
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
            MaxCouplerStrengthN = maxCouplerBreakN;
            MinCouplerStrengthN = minCouplerBreakN;
            CouplerStrengthScaleN = Math.Min(minCouplerBreakN, HighCouplerStrengthN) * 1.05f;
        }

        protected void UpdateCouplerForceImage(TrainCar car, int carPosition)
        {
            // the image has 19 icons, 0 is max push, 9 is neutral, 18 is max pull
            var idx = 9;
            var absForceN = Math.Abs(car.SmoothedCouplerForceUN);
            if (absForceN > 1000f && CouplerStrengthScaleN > 1000f)
            {
                // TODO: for push force, may need to scale differently (how?); containers derail at 300 klbf
                // TODO: may determine bar color to each car's coupler strength
                var relForce = absForceN / CouplerStrengthScaleN;
                var expForce = Math.Pow(9, relForce);
                idx = (int)Math.Floor(expForce);
                idx = (car.SmoothedCouplerForceUN > 0f) ? idx * -1 + 9 : idx + 9; // positive force is push
                if (idx < 0) { idx = 0; } else if (idx > 18) { idx = 18; }
            }
            if (car.WagonType == TrainCar.WagonTypes.Engine) { CouplerForceBarGraph[carPosition].Source = new Rectangle(1 + idx * BarGraphWidth, 0, BarGraphWidth, BarGraphHight); }
            else { CouplerForceBarGraph[carPosition].Source = new Rectangle(1 + idx * BarGraphWidth, BarGraphHight, BarGraphWidth, BarGraphHight); }
        }

        protected void UpdateCouplerImpulseImage(TrainCar car, int carPosition)
        {
            // the image has 19 icons, 0 is max push, 9 is neutral, 18 is max pull
            var idx = 9;
            var absImpulseN = Math.Abs(car.ImpulseCouplerForceUN);
            if (absImpulseN > 1000f && CouplerStrengthScaleN > 1000f)
            {
                // TODO: for push force, may need to scale differently (how?); containers derail at 300 klbf
                // TODO: may determine bar color to each car's coupler strength
                var relImpulse = absImpulseN / CouplerStrengthScaleN;
                var expImpulse = Math.Pow(9, relImpulse);
                idx = (int)Math.Floor(expImpulse);
                idx = (car.ImpulseCouplerForceUN > 0f) ? idx * -1 + 9 : idx + 9; // positive force is push
                if (idx < 0) { idx = 0; } else if (idx > 18) { idx = 18; }
            }
            if (car.WagonType == TrainCar.WagonTypes.Engine) { CouplerImpulseBarGraph[carPosition].Source = new Rectangle(1 + idx * BarGraphWidth, 0, BarGraphWidth, BarGraphHight); }
            else { CouplerImpulseBarGraph[carPosition].Source = new Rectangle(1 + idx * BarGraphWidth, BarGraphHight, BarGraphWidth, BarGraphHight); }
        }

        protected void UpdateDerailCoeffImage(TrainCar car, int carPosition)
        {
            var expForce = Math.Pow(9, car.DerailmentCoefficient);
            var idx = 8 + (int)Math.Floor(expForce);
            //var idx = 9 + (int)Math.Floor(car.DerailmentCoefficient * 9f);
            if (idx < 9) { idx = 9; } else if (idx > 18) { idx = 18; }
            if (car.WagonType == TrainCar.WagonTypes.Engine) { DerailCoeffBarGraph[carPosition].Source = new Rectangle(1 + idx * BarGraphWidth, 1, BarGraphWidth, HalfBarGraphHight); }
            else { DerailCoeffBarGraph[carPosition].Source = new Rectangle(1 + idx * BarGraphWidth, 1 + BarGraphHight, BarGraphWidth, HalfBarGraphHight); }
        }

        protected void UpdateSlackImage(TrainCar car, int carPosition)
        {
            // There is a CouplerSlack2M, but it seems to be static. HUD only uses CouplerSlackM.
            var idx = 9; // the image has 19 icons, 0 is max push, 9 is neutral, 18 is max pull
            var maxSlack = Math.Max(car.GetMaximumSimpleCouplerSlack1M(), car.GetMaximumSimpleCouplerSlack2M());
            if (maxSlack > 0f)
            {
                var slack = car.CouplerSlackM;
                var relSlack = slack / maxSlack * 9; // 9 bars
                idx = 9 + (int)Math.Floor(relSlack);
                if (idx < 0) { idx = 0; } else if (idx > 18) { idx = 18; }
            }
            if (car.WagonType == TrainCar.WagonTypes.Engine) { SlackBarGraph[carPosition].Source = new Rectangle(1 + idx * BarGraphWidth, 0, BarGraphWidth, BarGraphHight); }
            else { SlackBarGraph[carPosition].Source = new Rectangle(1 + idx * BarGraphWidth, BarGraphHight, BarGraphWidth, BarGraphHight); }
        }
    }
}
