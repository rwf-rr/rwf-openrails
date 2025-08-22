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

namespace Orts.Simulation.Signalling
{
    /// <summary>
    /// Represents the track circuit (signalling) view of a grade marker.
    /// </summary>
    public class TrackCircuitGradepost
    {
        /// <summary>Gradepost; is reference to objecty in Signals.GradepostList.</summary>
        public Gradepost GradepostRef;
        /// <summary>Gradepost location (distance) from each end of the section. Index 0 is from start, index 1 is from end.</summary>
        public float[] GradepostLocation = new float[2];
        /// <summary>Reference to grade post in TrItemTable; inxed into TrackDB.TrItemTable.</summary>
        public uint TrItemIdx;
        /// <summary>Reference to Track Node this gradepost is in; index into TrackDB.TrackNodes.</summary>
        public int TrackNodeIdx;

        public TrackCircuitGradepost(Gradepost thisRef, float distanceFromStart, float distanceFromEnd)
        {
            GradepostRef = thisRef;
            GradepostLocation[0] = distanceFromStart;
            GradepostLocation[1] = distanceFromEnd;
        }
    }
}
