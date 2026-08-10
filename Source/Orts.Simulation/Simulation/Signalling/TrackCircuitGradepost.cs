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
        /// <summary>Reference to objecty in Signals' GradepostList (by direction).</summary>
        public Gradepost GradepostRef;
        /// <summary>Gradepost location (distance) from the start of the section. End of section for the reverse direction.</summary>
        public float GradepostLocation;
        /// <summary>0 is in track circuit direction, 1 is in reverse direction.</summary>
        public int GradepostDirection;
        /// <summary>Reference to grade post in TrItemTable; index into TrackDB's TrItemTable.</summary>
        public uint TrItemIdx;
        /// <summary>Reference to Track Node this gradepost is in; index into TrackDB's TrackNodes.</summary>
        public int TrackNodeIdx;

        public TrackCircuitGradepost(Gradepost thisRef, float distanceFromStart, int dir)
        {
            GradepostRef = thisRef;
            GradepostLocation = distanceFromStart;
            GradepostDirection = dir;
        }
    }
}
