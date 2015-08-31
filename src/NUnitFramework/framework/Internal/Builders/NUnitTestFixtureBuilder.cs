// ***********************************************************************
// Copyright (c) 2014-2015 Charlie Poole
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// ***********************************************************************

using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework.Interfaces;

namespace NUnit.Framework.Internal.Builders
{
    /// <summary>
    /// NUnitTestFixtureBuilder is able to build a fixture given
    /// a class marked with a TestFixtureAttribute or an unmarked
    /// class containing test methods. In the first case, it is
    /// called by the attribute and in the second directly by
    /// NUnitSuiteBuilder.
    /// </summary>
    public class NUnitTestFixtureBuilder
    {
        #region Static Fields

        static readonly string NO_TYPE_ARGS_MSG =
            "Fixture type contains generic parameters. You must either provide " +
            "Type arguments or specify constructor arguments that allow NUnit " +
            "to deduce the Type arguments.";

        #endregion

        #region Instance Fields

        private ITestCaseBuilder _testBuilder = new DefaultTestCaseBuilder();

        #endregion

        #region Public Methods

        /// <summary>
        /// Build a TestFixture from type provided. A non-null TestSuite
        /// must always be returned, since the method is generally called
        /// because the user has marked the target class as a fixture.
        /// If something prevents the fixture from being used, it should
        /// be returned nonetheless, labelled as non-runnable.
        /// </summary>
        /// <param name="typeInfo">An ITypeInfo for the fixture to be used.</param>
        /// <returns>A TestSuite object or one derived from TestSuite.</returns>
        // TODO: This should really return a TestFixture, but that requires changes to the Test hierarchy.
        public TestSuite BuildFrom(ITypeInfo typeInfo)
        {
            var fixture = new TestFixture(typeInfo);

            if (fixture.RunState != RunState.NotRunnable)
                CheckTestFixtureIsValid(fixture);

            fixture.ApplyAttributesToTest(typeInfo.Type);

            AddTestCasesToFixture(fixture);

            return fixture;
        }

        /// <summary>
        /// Overload of BuildFrom called by tests that have arguments.
        /// Builds a fixture using the provided type and information 
        /// in the ITestFixtureData object.
        /// </summary>
        /// <param name="typeInfo">The TypeInfo for which to construct a fixture.</param>
        /// <param name="testFixtureData">An object implementing ITestFixtureData or null.</param>
        /// <returns></returns>
        public TestSuite BuildFrom(ITypeInfo typeInfo, ITestFixtureData testFixtureData)
        {
            Guard.ArgumentNotNull(testFixtureData, "testFixtureData");

            object[] arguments = testFixtureData.Arguments;

            if (typeInfo.Type.ContainsGenericParameters)
            {
                Type[] typeArgs = testFixtureData.TypeArgs;
                if (typeArgs.Length == 0)
                {
                    int cnt = 0;
                    foreach (object o in arguments)
                        if (o is Type) cnt++;
                        else break;

                    typeArgs = new Type[cnt];
                    for (int i = 0; i < cnt; i++)
                        typeArgs[i] = (Type)arguments[i];

                    if (cnt > 0)
                    {
                        object[] args = new object[arguments.Length - cnt];
                        for (int i = 0; i < args.Length; i++)
                            args[i] = arguments[cnt + i];

                        arguments = args;
                    }
                }

                if (typeArgs.Length > 0 ||
                    TypeHelper.CanDeduceTypeArgsFromArgs(typeInfo.Type, arguments, ref typeArgs))
                {
                    typeInfo = new TypeInfo(TypeHelper.MakeGenericType(typeInfo.Type, typeArgs));
                }
            }

            var fixture = new TestFixture(typeInfo);
            
            if (arguments != null && arguments.Length > 0)
            {
                string name = fixture.Name = TypeHelper.GetDisplayName(typeInfo.Type, arguments);
                string nspace = typeInfo.Namespace;
                fixture.FullName = nspace != null && nspace != ""
                    ? nspace + "." + name
                    : name;
                fixture.Arguments = arguments;
            }

            if (fixture.RunState != RunState.NotRunnable)
                fixture.RunState = testFixtureData.RunState;

            foreach (string key in testFixtureData.Properties.Keys)
                foreach (object val in testFixtureData.Properties[key])
                    fixture.Properties.Add(key, val);

            if (fixture.RunState != RunState.NotRunnable)
                CheckTestFixtureIsValid(fixture);

            fixture.ApplyAttributesToTest(typeInfo.Type);

            AddTestCasesToFixture(fixture);

            return fixture;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Method to add test cases to the newly constructed fixture.
        /// </summary>
        /// <param name="fixture">The fixture to which cases should be added</param>
        private void AddTestCasesToFixture(TestFixture fixture)
        {
            ITypeInfo typeInfo = fixture.TypeInfo;

            // TODO: Check this logic added from Neil's build.
            if (typeInfo.ContainsGenericParameters)
            {
                fixture.RunState = RunState.NotRunnable;
                fixture.Properties.Set(PropertyNames.SkipReason, NO_TYPE_ARGS_MSG);
                return;
            }

            var methods = typeInfo.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            foreach (IMethodInfo method in methods)
            {
                Test test = BuildTestCase(method, fixture);

                if (test != null)
                {
                    fixture.Add(test);
                }
            }
        }

        /// <summary>
        /// Method to create a test case from a MethodInfo and add
        /// it to the fixture being built. It first checks to see if
        /// any global TestCaseBuilder addin wants to build the
        /// test case. If not, it uses the internal builder
        /// collection maintained by this fixture builder.
        /// 
        /// The default implementation has no test case builders.
        /// Derived classes should add builders to the collection
        /// in their constructor.
        /// </summary>
        /// <param name="method">The method for which a test is to be created</param>
        /// <param name="suite">The test suite being built.</param>
        /// <returns>A newly constructed Test</returns>
        private Test BuildTestCase(IMethodInfo method, TestSuite suite)
        {
            return _testBuilder.CanBuildFrom(method.MethodInfo, suite)
                ? _testBuilder.BuildFrom(method, suite)
                : null;
        }

        private static void CheckTestFixtureIsValid(TestFixture fixture)
        {
            Type fixtureType = fixture.TypeInfo.Type;

            if (fixtureType.ContainsGenericParameters)
            {
                fixture.RunState = RunState.NotRunnable;
                fixture.Properties.Set(PropertyNames.SkipReason, NO_TYPE_ARGS_MSG);
            }
            else if (!IsStaticClass(fixtureType))
            {
                object[] args = fixture.Arguments;

                Type[] argTypes;

                // Note: This could be done more simply using
                // Type.EmptyTypes and Type.GetTypeArray() but
                // they don't exist in all runtimes we support.
                argTypes = new Type[args.Length];

                int index = 0;
                foreach (object arg in args)
                    argTypes[index++] = arg.GetType();

                ConstructorInfo ctor = fixtureType.GetConstructor(argTypes);

                if (ctor == null)
                {
                    fixture.RunState = RunState.NotRunnable;
                    fixture.Properties.Set(PropertyNames.SkipReason, "No suitable constructor was found");
                }
            }
        }

        private static bool IsStaticClass(Type type)
        {
            return type.IsAbstract && type.IsSealed;
        }

        #endregion
    }
}
