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

#region Design Notes
// This is a prototype to evaluate a train forces popup display. The
// intent is to provide real-time train-handling feeback of the forces
// within the train, particularly for long, heavy freight trains. The
// Forces HUD, display or browser, is hard to read.
// An alternative, better in the long-term, might be an external window
// that provides both in-train and over time feedback, as seen on
// professional train simulators. See the discussion in the Elvas tower
// forum, at:
// https://www.elvastower.com/forums/index.php?/topic/38056-proposal-for-train-forces-popup-display/
//
// Longitudinal Force:
//   Shows the length-wise pull or push force at each coupling, as a colored bar graph. Up
//   (positive) is pull, down (negative) is push. The scale is determined by the weakest
//   coupler in the train. The steps  are non-linear, to provide more sensitivity near the
//   breaking point.
//
// Lateral Force:
//   Shows the sideway push or pull at the wheels as a colored bar graph. Up (positive) is
//   pull to the inside (stringline), down (negative) is push to the outside (jackknife).
//   The scale is determined by the lowest axle-load (vertical force). The steps  are
//   non-linear, to provide more sensitivity near the derailing point.
//
// Bar Graph for Force:
//   +/- 9 bars; 4 green, 3 orange, 2 red
//   blue middle-bar is an engine, white is a car
//
// Slack:
//   Was considered, but is sufficiently reflected by the lateral force display.
//
// Break Pipe Pressure or Brake Force:
//   Was considered. It is not really a train-handling parameter.
//
// Grade & Curvature:
//   It is not practical to show grade or curvature in a meaningfule way
//   without needing significant screen-space and calculations. Thus they
//   are not included.
//
// Notes:
//   * Design was copied from the old (horizontal) train operations window.
//   * The derail coefficient was considered, but the lateral force provides a more uniform
//     view across the train.
//   * Using text-hight for field-width, as text width is variable.
//   * Lateral Forces and Derailment:
//     - As of Feb 2025, lateral forces are not calculated on straight track.
//     - As of Feb 2025, longitudinal buff forces may also cause coupler breaks.
#endregion

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using ORTS.Common;
using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Xml;
using SharpDX.Direct2D1.Effects;

namespace Orts.Viewer3D.Popups
{
    public class TrainForcesWindow : Window
    {
        const float HighCouplerStrengthN = 2.2e6f; // 500 klbf
        const float ImpossiblyHighForce = 9.999e8f;

        static Texture2D ForceBarTextures;
        const int BarGraphHight = 40;
        const int HalfBarGraphHight = 20;
        const int BarWidth = 6;

        float SimSeconds = 0f;
        float LastLogSeconds = 0f;
        int PrepareCalls = 0;

        Train PlayerTrain;
        int LastPlayerTrainCars;
        bool LastPlayerLocomotiveFlippedState;

        float MinCouplerStrengthN = ImpossiblyHighForce;
        float CouplerStrengthScaleN;

        float MinDerailForceN = ImpossiblyHighForce;
        float DerailForceScaleN;

        Image[] CouplerForceBarGraph;
        Image[] WheelForceBarGraph;

        Label MaxLongForceForTextBox;
        Label MaxLatForceForTextBox;

        float LastAbsForceN = 0f;
        float NextLowerTime = 0f;

        /// <summary>
        /// Constructor. Window is wide enough for about 150 cars. Longer trains
        /// have a scrollbar. This seems a reasonable compromise between typical
        /// display size and typical train length.
        /// </summary>
        public TrainForcesWindow(WindowManager owner)
            : base(owner, Window.DecorationSize.X + 1000, Window.DecorationSize.Y + owner.TextFontDefault.Height * 2 + BarGraphHight * 2 + 20, Viewer.Catalog.GetString("Train Forces"))
        {
        }

        /// <summary>
        /// Initialize display. Loads static data, such as the bar graph images.
        /// </summary>
        protected internal override void Initialize()
        {
            base.Initialize();
            if (ForceBarTextures == null)
            {
                ForceBarTextures = SharedTextureManager.Get(Owner.Viewer.RenderProcess.GraphicsDevice, System.IO.Path.Combine(Owner.Viewer.ContentPath, "BarGraph.png"));
            }
        }

        /// <summary>
        /// Create the layout. Defines the components within the window.
        /// </summary>
        protected override ControlLayout Layout(ControlLayout layout)
        {
            var textHeight = Owner.TextFontDefault.Height;
            var labelWidth = textHeight * 6;
            int numBars = 60;  // enough to show the important part of the text line
            if (PlayerTrain != null && PlayerTrain.Cars != null && PlayerTrain.Cars.Count > numBars) { numBars = PlayerTrain.Cars.Count; }
            var innerBoxWidth = labelWidth + BarWidth * numBars + 4;

            var hbox = base.Layout(layout).AddLayoutHorizontal();
            var scrollbox = hbox.AddLayoutScrollboxHorizontal(hbox.RemainingHeight);
            var vbox = scrollbox.AddLayoutVertical(Math.Max(innerBoxWidth,scrollbox.RemainingWidth));
            var longForceBox = vbox.AddLayoutHorizontal(BarGraphHight + 4);
            longForceBox.Add(new Label(0, (BarGraphHight - textHeight) / 2, labelWidth, BarGraphHight, Viewer.Catalog.GetString("Longitudinal") + ": "));
            var latForceBox = vbox.AddLayoutHorizontal(BarGraphHight + 4);
            latForceBox.Add(new Label(0, (BarGraphHight - textHeight) / 2, labelWidth, BarGraphHight, Viewer.Catalog.GetString("Lateral") + ": "));

            if (PlayerTrain != null)
            {
                SetConsistProperties(PlayerTrain);

                CouplerForceBarGraph = new Image[PlayerTrain.Cars.Count];
                WheelForceBarGraph = new Image[PlayerTrain.Cars.Count];

                int carPosition = 0;
                foreach (var car in PlayerTrain.Cars)
                {
                    longForceBox.Add(CouplerForceBarGraph[carPosition] = new Image(BarWidth, BarGraphHight));
                    CouplerForceBarGraph[carPosition].Texture = ForceBarTextures;
                    UpdateCouplerForceImage(car, carPosition);

                    latForceBox.Add(WheelForceBarGraph[carPosition] = new Image(BarWidth, BarGraphHight));
                    WheelForceBarGraph[carPosition].Texture = ForceBarTextures;
                    UpdateWheelForceImage(car, carPosition);

                    carPosition++;
                }

                vbox.AddHorizontalSeparator();
                var textbox = vbox.AddLayoutHorizontalLineOfText();
                textbox.Add(new Label(textHeight * 9, textHeight, Viewer.Catalog.GetString("Max Long Force") + ": ", LabelAlignment.Right));
                textbox.Add(MaxLongForceForTextBox = new Label(textHeight * 7, textHeight, FormatStrings.FormatLargeForce(0f, false), LabelAlignment.Right));
                textbox.Add(new Label(textHeight * 9, textHeight, Viewer.Catalog.GetString("Max Lat Force") + ": ", LabelAlignment.Right));
                textbox.Add(MaxLatForceForTextBox = new Label(textHeight * 7, textHeight, FormatStrings.FormatLargeForce(0f, false), LabelAlignment.Right));

                textbox.Add(new Label(textHeight * 8, textHeight, Viewer.Catalog.GetString("Min Coupler") + ": ", LabelAlignment.Right));
                textbox.Add(new Label(textHeight * 5, textHeight, FormatStrings.FormatLargeForce(MinCouplerStrengthN, false), LabelAlignment.Right));
                textbox.Add(new Label(textHeight * 8, textHeight, Viewer.Catalog.GetString("Min Derail") + ": ", LabelAlignment.Right));
                textbox.Add(new Label(textHeight * 5, textHeight, FormatStrings.FormatLargeForce(MinDerailForceN, false), LabelAlignment.Right));

                LastAbsForceN = 0f;
            }

            return hbox;
        }

        /// <summary>
        /// Prepare frame for rendering. Update the data (graphs and values in text box).
        /// </summary>
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
                var absMaxLongForceN = 0.0f; var longForceSign = 1.0f; var maxLongForceCarNum = 0;
                var absMaxLatForceN = 0.0f; var latForceSign = 1.0f; var maxLatForceCarNum = 0;

                int carPosition = 0;
                foreach (var car in PlayerTrain.Cars)
                {
                    UpdateCouplerForceImage(car, carPosition);
                    UpdateWheelForceImage(car, carPosition);

                    var longForceN = car.CouplerForceU; var absLongForceN = Math.Abs(longForceN);
                    if (absLongForceN > absMaxLongForceN) { absMaxLongForceN = absLongForceN; longForceSign = longForceN > 0 ? -1.0f : 1.0f; maxLongForceCarNum = carPosition + 1; }

                    // see TrainCar.UpdateTrainDerailmentRisk()
                    var absLatForceN = car.TotalWagonLateralDerailForceN;
                    if (car.WagonNumBogies <= 0 || car.GetWagonNumAxles() <= 0) { absLatForceN = car.DerailmentCoefficient * DerailForceScaleN; }
                    if (absLatForceN > absMaxLatForceN) { absMaxLatForceN = absLatForceN; latForceSign = (car.CouplerForceU > 0 && car.CouplerSlackM < 0) ? -1.0f : 1.0f; maxLatForceCarNum = carPosition + 1; }

                    carPosition++;
                }

                // update max coupler force; TODO: smooth the downslope 
                if (MaxLongForceForTextBox != null)
                {
                    MaxLongForceForTextBox.Text = FormatStrings.FormatLargeForce(absMaxLongForceN * longForceSign, false) + string.Format("  ({0})", maxLongForceCarNum);
                }

                // update max derail force
                if (MaxLatForceForTextBox != null)
                {
                    MaxLatForceForTextBox.Text = FormatStrings.FormatLargeForce(absMaxLatForceN * latForceSign, false) + string.Format("  ({0})", maxLatForceCarNum);
                }
            }
        }

        /// <summary>
        /// Get static force values from consist, such as coupler strength and
        /// force that causes the wheel to derail.
        /// </summary>
        protected void SetConsistProperties(Train theTrain)
        {
            float minCouplerBreakN = ImpossiblyHighForce;
            float minDerailForceN = ImpossiblyHighForce;

            foreach (var car in theTrain.Cars)
            {
                if (car is MSTSWagon wag)
                {
                    var couplerBreakForceN = wag.GetCouplerBreak2N() > 1.0f ? wag.GetCouplerBreak2N() : wag.GetCouplerBreak1N();
                    if (couplerBreakForceN < minCouplerBreakN) { minCouplerBreakN = couplerBreakForceN; }

                    // simplified from TrainCar.UpdateTrainDerailmentRisk()
                    var numWheels = wag.GetWagonNumAxles() * 2; 
                    if (numWheels <= 0) { numWheels = 4; }  // err towards higher vertical force
                    var wheelDerailForceN = wag.MassKG / numWheels * wag.GetGravitationalAccelerationMpS2();
                    if (wheelDerailForceN > 1000f)  // exclude improbable vales
                    {
                        if (wheelDerailForceN < minDerailForceN) { minDerailForceN = wheelDerailForceN; }
                    }
                }
            }
            MinCouplerStrengthN = minCouplerBreakN;
            CouplerStrengthScaleN = Math.Min(minCouplerBreakN, HighCouplerStrengthN) * 1.05f;

            MinDerailForceN = minDerailForceN;
            DerailForceScaleN = Math.Min(minDerailForceN, HighCouplerStrengthN) * 1.05f;
        }

        /// <summary>
        /// Update the coupler force (longitudinal) icon for a car. The image has 19 icons;
        /// index 0 is max push, 9 is neutral, 18 is max pull.
        /// </summary>
        protected void UpdateCouplerForceImage(TrainCar car, int carPosition)
        {
            var idx = 9;  // neutral
            var absForceN = Math.Abs(car.SmoothedCouplerForceUN);

            if (absForceN > 1000f && CouplerStrengthScaleN > 1000f)  // exclude improbabl values
            {
                // power scale, to be sensitve at limit:  1k lbf, 32%, 50%, 63%, 73%, 82%, 89%, 95%, 100%
                var relForce = absForceN / CouplerStrengthScaleN;
                var expForce = Math.Pow(9, relForce);
                idx = (int)Math.Floor(expForce);
                idx = (car.SmoothedCouplerForceUN > 0f) ? idx * -1 + 9 : idx + 9; // positive force is push
                if (idx < 0) { idx = 0; } else if (idx > 18) { idx = 18; }
                // TODO: for push force, may need to scale differently (how?); containers derail at 300 klbf
            }

            if (car.WagonType == TrainCar.WagonTypes.Engine) { CouplerForceBarGraph[carPosition].Source = new Rectangle(1 + idx * BarWidth, 0, BarWidth, BarGraphHight); }
            else { CouplerForceBarGraph[carPosition].Source = new Rectangle(1 + idx * BarWidth, BarGraphHight, BarWidth, BarGraphHight); }
        }

        /// <summary>
        /// Update the wheel force (lateral) icon for a car. The image has 19 icons;
        /// index 0 is max push (outside), 9 is neutral, 18 is max pull (inside).
        /// </summary>
        protected void UpdateWheelForceImage(TrainCar car, int carPosition)
        {
            var idx = 9;  // neutral

            var absForceN = car.TotalWagonLateralDerailForceN;

            // see TrainCar.UpdateTrainDerailmentRisk()
            if (car.WagonNumBogies <= 0 || car.GetWagonNumAxles() <= 0)
            {
                absForceN = car.DerailmentCoefficient * DerailForceScaleN;
                if (car.CouplerForceU > 0 && car.CouplerSlackM < 0) { absForceN /= 1.77f; }  // push to outside
                else { absForceN /= 1.34f; }  // pull to inside
            }

            // see TrainCar.UpdateTrainDerailmentRisk()
            float directionalScaleN = DerailForceScaleN;
            if (car.CouplerForceU > 0 && car.CouplerSlackM < 0) { directionalScaleN /= 1.77f;  }  // push to outside
            else if (car.CouplerForceU < 0 && car.CouplerSlackM > 0) { directionalScaleN /= 1.34f; }  // pull to inside

            #region Debug
            // debug
            if (car.DerailmentCoefficient > 1.0f)
            {
                var numWheels = car.GetWagonNumAxles() * 2; if (numWheels <= 0) { numWheels = 4; }
                var wheelDerailForceN = car.MassKG / numWheels * car.GetGravitationalAccelerationMpS2();

                Debug.WriteLine("DebugCoeff > 1: car-no {0}, car-id {1}, mass {2}, axles {3}, bogies {10}, vertical {4}, lateral {5}, Coeff {6}, limit {7}, abs {8}, scale {9}",
                    carPosition, car.CarID, FormatStrings.FormatLargeMass(car.MassKG, false, false), car.GetWagonNumAxles(), FormatStrings.FormatLargeForce(car.TotalWagonVerticalDerailForceN, false),
                    FormatStrings.FormatLargeForce(car.TotalWagonLateralDerailForceN, false), car.DerailmentCoefficient, FormatStrings.FormatLargeForce(wheelDerailForceN, false),
                    FormatStrings.FormatLargeForce(absForceN, false), FormatStrings.FormatLargeForce(DerailForceScaleN, false), car.WagonNumBogies);
            }
            #endregion

            if (absForceN > 1000f && DerailForceScaleN > 1000f)  // exclude improbable values
            {
                // flatter scale due to discrete curve radus: 1k lbf, 21%, 37%, 51%, 64%, 74%, 84%, 93%, 100%
                var relForce = absForceN / DerailForceScaleN;
                // var expForce = Math.Pow(9, relForce);
                var expForce = (Math.Pow(3, relForce) - 1) * 4 + 1;
                idx = (int)Math.Floor(expForce);
                idx = (car.CouplerForceU > 0f && car.CouplerSlackM < 0) ? idx * -1 + 9 : idx + 9; // positive force is push
                if (idx < 0) { idx = 0; } else if (idx > 18) { idx = 18; }
            }

            #region Debug
            // debug
            if (idx == 0 || idx == 18)
            {
                var numWheels = car.GetWagonNumAxles() * 2; if (numWheels <= 0) { numWheels = 4; }
                var wheelDerailForceN = car.MassKG / numWheels * car.GetGravitationalAccelerationMpS2();

                Debug.WriteLine("Idx at boundary: car-no {0}, car-id {1}, mass {2}, axles {3}, bogies {10}, vertical {4}, lateral {5}, Coeff {6}, limit {7}, abs {8}, scale {9}",
                    carPosition, car.CarID, FormatStrings.FormatLargeMass(car.MassKG, false, false), car.GetWagonNumAxles(), FormatStrings.FormatLargeForce(car.TotalWagonVerticalDerailForceN, false),
                    FormatStrings.FormatLargeForce(car.TotalWagonLateralDerailForceN, false), car.DerailmentCoefficient, FormatStrings.FormatLargeForce(wheelDerailForceN, false),
                    FormatStrings.FormatLargeForce(absForceN, false), FormatStrings.FormatLargeForce(DerailForceScaleN, false), car.WagonNumBogies);
            }
            #endregion

            if (car.WagonType == TrainCar.WagonTypes.Engine) { WheelForceBarGraph[carPosition].Source = new Rectangle(1 + idx * BarWidth, 0, BarWidth, BarGraphHight); }
            else { WheelForceBarGraph[carPosition].Source = new Rectangle(1 + idx * BarWidth, BarGraphHight, BarWidth, BarGraphHight); }
        }
    }
}
