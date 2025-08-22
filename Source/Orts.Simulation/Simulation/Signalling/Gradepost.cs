// COPYRIGHT 2025 by the Open Rails project.
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

// This is part of the effort to respresent grade information in the track monitor.
// The initial version determinates significant grade changes from the track database.
//
// Milempost are taken as a model for presenting the grade information in the simulation.
// This will allow, in the future, to add grade-posts to the track, as is the case at some
// railways (eg. in Switzerland).

namespace Orts.Simulation.Signalling
{
    /// <summary>
    /// Represents either a world-object grade post, or a calculated grade post.
    /// Only the latter is currently supported.
    /// </summary>
    public class Gradepost
    {
        /// <summary>Reference to TrItem; index into TrackDB.TrItemTable.</summary>
        public uint TrItemId;
        /// <summary>Reference to TrackCircuitSection; index into Signals.TrackCircuitList.</summary>
        public int TCReference = -1;  // undefined
        /// <summary>Position within TrackCircuit. Distance im meters?</summary>
        public float TCOffset;
        /// <summary>Grade in percent. Index 0 is in track circuit direction, index 1 is in reverse direction</summary>
        public float[] GradePct = new float[2];
        /// <summary>Distance in meters for which the grade applies. Index 0 is in track circuit direction, index 1 is in reverse direction.</summary>
        public float[] ForDistanceM = new float[2];
        /// <summary>Reference to TrackNode; index into TrackDB.TrackNodes.</summary>
        public int TrackNodeIdx;

        /// <summary>Constructor with base attributes.</summary>
        public Gradepost(uint trItemId, float forwardGradePct, float reverseGradePct, float forwardDistanceM, float reverseDistanceM)
        {
            TrItemId = trItemId;
            GradePct[0] = forwardGradePct; GradePct[1] = reverseGradePct;
            ForDistanceM[0] = forwardDistanceM; ForDistanceM[1] = reverseDistanceM;
        }

        /// <summary>Dummy constructor</summary>
        public Gradepost()
        {
        }
    }
}
