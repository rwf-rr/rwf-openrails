﻿// COPYRIGHT 2015 by the Open Rails project.
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

using Orts.Formats.Msts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ORTS.ContentManager.Models
{
    public class Consist
    {
        public readonly string Name;
        public readonly string NumEngines; // using "+" between DPU sets
        public readonly string NumCars;
        public readonly float MaxSpeedMps;
        public readonly float LengthM = 0F;
        public readonly int NumAxles = 0;
        public readonly float MassKG = 0F;
        public readonly float TrailingMassKG = 0F;
        public readonly float MaxPowerW = 0F;
        public readonly float MaxTractiveForceN = 0F;
        public readonly float MaxContinuousTractiveForceN = 0F;
        public readonly float MaxDynamicBrakeForceN = 0F;
        public readonly float MaxBrakeForce = 0F;
        public readonly int NumOperativeBrakes = 0;
        public readonly float MinCouplerStrengthN = 9.999e8f;  // impossible high force
        public readonly float MinDerailForceN = 9.999e8f;  // impossible high force

        public readonly IEnumerable<Car> Cars;

        public Consist(Content content)
        {
            Debug.Assert(content.Type == ContentType.Consist);
            if (System.IO.Path.GetExtension(content.PathName).Equals(".con", StringComparison.OrdinalIgnoreCase))
            {
                var file = new ConsistFile(content.PathName);
                Name = file.Name;
                MaxSpeedMps = file.Train.TrainCfg.MaxVelocity.A;

                var EngCount = 0;
                var WagCount = 0;
                var Separator = ""; // when set, indicates that subsequent engines are in a separate block

                var basePath = System.IO.Path.Combine(System.IO.Path.Combine(content.Parent.PathName, "Trains"), "Trainset");

                var CarList = new List<Car>();
                foreach (Wagon wag in file.Train.TrainCfg.WagonList)
                {
                    float wagonMassKG = 0; int numDriveAxles = 0; int numIdleAxles = 0; int numAllAxles = 0;
                    try
                    {
                        var fileType = wag.IsEngine ? ".eng" : ".wag";
                        var filePath = System.IO.Path.Combine(System.IO.Path.Combine(basePath, wag.Folder), wag.Name + fileType);
                        var wagonFile = new WagonFile(filePath);
                        var engFile = wag.IsEngine ? new EngineFile(filePath) : null;

                        LengthM += wagonFile.WagonSize.LengthM;
                        MassKG += wagonFile.MassKG;
                        wagonMassKG = wagonFile.MassKG;
                        MaxBrakeForce += wagonFile.MaxBrakeForceN;
                        MinCouplerStrengthN = Math.Min(MinCouplerStrengthN, wagonFile.MinCouplerStrengthN);
                        var subType = wagonFile.WagonType;

                        if (engFile != null)
                        {
                            subType = engFile.EngineType;

                            // see MSTSLocomotive.Initialize()
                            numDriveAxles = engFile.NumDriveAxles;
                            if (numDriveAxles == 0)
                            {
                                if (engFile.NumEngWheels != 0 && engFile.NumEngWheels < 7) { numDriveAxles = (int)engFile.NumEngWheels; }
                                else { numDriveAxles = 4; }
                            }

                            if (engFile.MaxForceN > 25000)  // exclude legacy driving trailers / cab-cars
                            {
                                EngCount++;
                                MaxPowerW += engFile.MaxPowerW;
                                MaxTractiveForceN += engFile.MaxForceN;
                                MaxContinuousTractiveForceN += engFile.MaxContinuousForceN > 0f ? engFile.MaxContinuousForceN : engFile.MaxForceN;
                                MaxDynamicBrakeForceN += engFile.MaxDynamicBrakeForceN;
                            }
                            else { WagCount++; }
                        }
                        else if (!wag.IsEOT && wagonFile.WagonSize.LengthM > 1.1) // exclude legacy EOT
                        {
                            WagCount++;
                            TrailingMassKG += wagonFile.MassKG;
                            if (wagonFile.MaxBrakeForceN > 0 && wagonFile.BrakeSystemType != null && !wagonFile.BrakeSystemType.Contains("manual_braking") &&
                                !wagonFile.BrakeSystemType.Contains("air_piped") && !wagonFile.BrakeSystemType.Contains("vacuum_piped"))
                            {
                                NumOperativeBrakes++;
                            }
                        }

                        // see MSTSWagon.LoadFromWagFile()
                        numIdleAxles = wagonFile.NumWagAxles;
                        if (numIdleAxles == 0 && !wag.IsEngine)
                        {
                            if (wagonFile.NumWagWheels != 0 && wagonFile.NumWagWheels < 6) { numIdleAxles = (int)wagonFile.NumWagWheels; }
                            else { numIdleAxles = 4; }
                        }

                        // correction for steam engines; see TrainCar.Update()
                        // this is not always correct as TrainCar uses the WheelAxles array for the count; that is too complex to do here
                        if (subType.Equals("Steam") && numDriveAxles >= (numDriveAxles + numIdleAxles)) { numDriveAxles /= 2; }

                        // see TrainCar.UpdateTrainDerailmentRisk(), ~ line 1609
                        numAllAxles = numDriveAxles + numIdleAxles;

                        // exclude legacy EOT from total axle count
                        if (!wag.IsEOT && wagonFile.WagonSize.LengthM > 1.1)
                        {
                            NumAxles += numAllAxles;
                        }

                        if (numAllAxles > 0 && wagonFile.MassKG > 1000)
                        {
                            const float GravitationalAccelerationMpS2 = 9.80665f;
                            var derailForce = wagonFile.MassKG / numAllAxles / 2f * GravitationalAccelerationMpS2;
                            if (derailForce > 1000f) { MinDerailForceN = Math.Min(MinDerailForceN, derailForce); }
                        }
                    }
                    catch (IOException e) // continue without details when eng/wag file does not exist
                    {
                        if (wag.IsEngine) { EngCount++; } else { WagCount++; }
                    }

                    if (!wag.IsEngine && EngCount > 0)
                    {
                        NumEngines = NumEngines + Separator + EngCount.ToString();
                        EngCount = 0; Separator = "+";
                    }

                    CarList.Add(new Car(wag, wagonMassKG));
                }
                if (EngCount > 0) { NumEngines = NumEngines + Separator + EngCount.ToString(); }
                if (NumEngines == null) { NumEngines = "0"; }
                NumCars = WagCount.ToString();
                Cars = CarList;
            }
        }

        public enum Direction{
            Forwards,
            Backwards,
        }

        public class Car
        {
            public readonly string ID;
            public readonly string Name;
            public readonly Direction Direction;
            public readonly bool IsEngine;
            public readonly float MassKG;

            internal Car(Wagon car, float massKg)
            {
                ID = car.UiD.ToString();
                Name = car.Folder + "/" + car.Name;
                Direction = car.Flip ? Consist.Direction.Backwards : Consist.Direction.Forwards;
                IsEngine = car.IsEngine;
                MassKG = massKg;
            }
        }
    }
}
