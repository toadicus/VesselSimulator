// 
//     Kerbal Engineer Redux
// 
//     Copyright (C) 2014 CYBUTEK
// 
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
// 
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
// 
//     You should have received a copy of the GNU General Public License
//     along with this program.  If not, see <http://www.gnu.org/licenses/>.
// 

using System;
using System.Collections.Generic;
using KerbalEngineer.VesselSimulator;
using UnityEngine;

namespace KerbalEngineer
{
    // a (force, application point) tuple
    public class AppliedForce
    {
        private static readonly Pool<AppliedForce> pool = new Pool<AppliedForce>(Create, Reset);

        public Vector3 vector;
        public Vector3 applicationPoint;

        static private AppliedForce Create()
        {
            return new AppliedForce();
        }

        static private void Reset(AppliedForce appliedForce) { }

        static public AppliedForce New(Vector3 vector, Vector3 applicationPoint)
        {
            AppliedForce force = pool.Borrow();
            force.vector = vector;
            force.applicationPoint = applicationPoint;
            return force;
        }

        public void Release()
        {
            pool.Release(this);
        }


    }

	// This class was mostly adapted from FARCenterQuery, part of FAR, by ferram4, GPLv3
	// https://github.com/ferram4/Ferram-Aerospace-Research/blob/master/FerramAerospaceResearch/FARCenterQuery.cs
    // Also see https://en.wikipedia.org/wiki/Resultant_force

	// It accumulates forces and their points of applications, and provides methods for
    // calculating the effective torque at any position, as well as the minimum-torque net force application point.
    //
    // The latter is a non-trivial issue; there is a 1-dimensional line of physically-equivalent solutions parallel
    // to the resulting force vector; the solution closest to the weighted average of force positions is chosen.
	// In the case of non-parallel forces, there usually is an infinite number of such lines, all of which have
	// some amount of residual torque. The line with the least amount of residual torque is chosen.
	public class ForceAccumulator
	{
	    // Total force.
		private Vector3 totalForce = Vector3.zero;
		// Torque needed to compensate if force were applied at origin.
		private Vector3 totalZeroOriginTorque = Vector3.zero;

		// Weighted average of force application points.
		private WeightedVectorAverager avgApplicationPoint = new WeightedVectorAverager();

		// Feed an force to the accumulator.
		public void AddForce(Vector3 applicationPoint, Vector3 force)
		{
			totalForce += force;
			totalZeroOriginTorque += Vector3.Cross(applicationPoint, force);
			avgApplicationPoint.Add(applicationPoint, force.magnitude);
		}

        public Vector3 GetAverageForceApplicationPoint() {
            return avgApplicationPoint.Get();
        }

        public void AddForce(AppliedForce force) {
            AddForce(force.applicationPoint, force.vector);
        }

		// Residual torque for given force application point.
		public Vector3 TorqueAt(Vector3 origin)
		{
			return totalZeroOriginTorque - Vector3.Cross(origin, totalForce);
		}

        // Total force vector.
        public Vector3 GetTotalForce()
        {
            return totalForce;
        }

        // Returns the minimum-residual-torque force application point that is closest to origin.
        // Note that TorqueAt(GetMinTorquePos()) is always parallel to totalForce.
        public Vector3 GetMinTorqueForceApplicationPoint(Vector3 origin)
        {
            double fmag = totalForce.sqrMagnitude;
            if (fmag <= 0) {
                return origin;
            }

            return origin + Vector3.Cross(totalForce, TorqueAt(origin)) / (float)fmag;
        }

        public Vector3 GetMinTorqueForceApplicationPoint()
        {
            return GetMinTorqueForceApplicationPoint(avgApplicationPoint.Get());
        }

	    public void Reset()
	    {
	        totalForce = Vector3.zero;
	        totalZeroOriginTorque = Vector3.zero;
            avgApplicationPoint.Reset();
	    }
	}
}