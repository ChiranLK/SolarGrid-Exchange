/*
 * AssemblyInfo.cs
 * -----------------------------------------------------------------------------
 * Purpose : Grants the focused Component 3 policy verification project access
 *           to internal read-policy members without widening the production API.
 * -----------------------------------------------------------------------------
 */

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("SolarMicrogrid.API.PolicyTests")]
