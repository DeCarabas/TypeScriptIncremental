using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Doty.Spec
{
    /// <summary>A small class that helps you write tests that are easier to read and understand.</summary>
    /// <remarks><para>What is this?</para>
    /// 
    /// <para>This is an instance of a thing that comes from the world of "Behavior-Driven Development". BDD is an 
    /// opinionated refinement of TDD: where TDD simply tells you to write your tests before you write your code, BDD
    /// has opinions about just how those tests are written.</para>
    /// 
    /// <para>To use this small class effectively, you should structure your tests in a particular way. First, you
    /// should have a relatively small number of test methods. Because you XSpec runs all of the assertions it can 
    /// before it quits, you should have no fear of describing as much of the behavior of a class or method in a single 
    /// test as you can. In this way, when you are modifying the behavior of an existing class or method, you can 
    /// easily find a natural place to add or modify an assertion.</para>
    /// 
    /// <para>Secondly, you should describe the behavior you want the system to have before filling in the various
    /// lambda functions. As a convenience, use the overloads which do not have delegates-- those will compile, but
    /// will not pass. You can then fill in the delegates as you write the code to implement them. Remember, the spec
    /// runs as much as it can at any given time, so you can write code and run the test incrementally until the whole
    /// thing passes.</para>
    /// 
    /// <para>Thirdly, try to keep your actions in .When() and .It() calls to a single line. Your code0 will read a lot
    /// better, and you'll be more inclined to write finer-grained assertions.</para>
    /// 
    /// <para>For more information about TDD, try <a href="http://dannorth.net/introducing-bdd/">this article</a>, or
    /// possibly wikipedia.</para>
    /// </remarks>
    /// <example>
    /// <code>
    ///[TestMethod]
    ///public void WhenIncrementingAnInteger()
    ///{
    ///    int x = 0;
    ///    XSpec.Given( "an integer, set to zero", () => x = 0 )
    ///        .When( "the integer is incremented", () => x++ )
    ///            .It( "can be incremented again to two", () => { x++; Assert.AreEqual( x, 2 ); } )
    ///            .It( "should be 1", () => x == 1 )
    ///        .When( "the integer is incremented again", () => x++ )
    ///            .It( "should be 2", () => x == 2 )
    ///        .When( "the integer is divided by zero", () => x = x / ( x - 2 ) )
    ///            .ItShouldThrow&lt;DivideByZeroException>()
    ///    .Go();
    ///}        
    /// </code>
    /// </example>
    [DebuggerNonUserCode]
    public class XSpec
    {
        readonly Action action;
        readonly List<XSpec> children = new List<XSpec>();
        readonly string description;
        Exception exception;
        readonly XSpec parent;
        readonly List<Tuple<long, Exception>> results = new List<Tuple<long, Exception>>();
        ExecState state;
        bool swallowExceptions;
        readonly NodeType type;

        XSpec( string description, Action action, XSpec parent, NodeType type )
        {
            this.action = action;
            this.description = description;
            this.parent = parent;
            this.type = type;
        }

        /// <summary>
        /// Gets or sets the default spec execution policy for this appdomain.
        /// </summary>
        /// <remarks>
        /// The value of this propery influences what happens when you call the <see cref="Go"/> method.
        /// </remarks>
        public static SpecExecutionPolicy DefaultExecutionPolicy { get; set; }

        /// <summary>
        /// Adds a new node to the this spec. The new node may or may not be a child of this one.
        /// </summary>
        /// <param name="description">The description associated with the node.</param>
        /// <param name="action">The action associated with the node.</param>
        /// <param name="type">The type of the node.</param>
        /// <returns>The new node.</returns>
        XSpec AddNode( string description, Action action, NodeType type )
        {
            return AddNode( description, action, type, type );
        }

        /// <summary>
        /// Adds a new node to the this spec, with an explicit precedence override. The new node may or may not be a 
        /// child of this one.
        /// </summary>
        /// <param name="description">The description associated with the node.</param>
        /// <param name="action">The action associated with the node.</param>
        /// <param name="type">The type of the node.</param>
        /// <param name="precedence">The type to use as the precedence of this node. This parameter exists specifically
        /// so that 'TheException' and 'It' nodes can be peers in the tree, rather than one being the child of the 
        /// other.</param>
        /// <returns>The new node.</returns>
        XSpec AddNode( string description, Action action, NodeType type, NodeType precedence )
        {
            XSpec parent = this;
            while ( parent.type >= precedence )
            {
                parent = parent.parent;
            }

            var node = new XSpec( description, action, parent, type );
            parent.children.Add( node );
            return node;
        }

        bool Exec()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                this.action();
            }
            catch ( Exception e )
            {
                this.exception = e;
            }
            stopwatch.Stop();

            ExecState resultState;
            if ( this.exception != null && !this.swallowExceptions )
            {
                if ( this.exception is AssertFailedException || this.exception is AssertInconclusiveException )
                {
                    resultState = ExecState.Failed;
                }
                else
                {
                    resultState = ExecState.Exception;
                }
            }
            else
            {
                resultState = ExecState.Passed;
            }

            this.results.Add( Tuple.Create( stopwatch.ElapsedMilliseconds, this.exception ) );
            if ( this.state <= resultState ) { this.state = resultState; }
            return ( resultState == ExecState.Passed || this.swallowExceptions );
        }

        /// <summary>
        /// Begins a new specification, by specifying the initial conditions. Execute the specification by calling 
        /// <see cref="Go"/>.
        /// </summary>
        /// <param name="description">A string describing the initial conditions.</param>
        /// <param name="action">The action to run to set up the initial conditions. This action should completely
        /// reset the state, regardless of what the state was before being executed.</param>
        /// <returns>A new specification, which will execute under the initial conditions.</returns>
        /// <remarks>The <see cref="description"/> parameter should be a short sentence that starts with 'Given ...', 
        /// but without the word 'Given'. The first reason for this is that the code should be legible. e.g., this:
        /// <code>
        ///   .Given( "a choice between dancing pigs or security", () => { choice = new Choice("dancing pigs", "security"); } )
        /// </code>
        /// <para>reads like "Given a choice between dancing pigs or security..."</para>
        /// <para>The second reason is that the string is printed out as part of the test results, and the word "Given"
        /// is prepended to the description.</para>
        /// <para>The provided delegate will be run many times; once for each assertion. It should completely reset
        /// the state of the world to the appropriate initial state, so that side effects caused by any given assertion
        /// are undone.</para></remarks>
        /// <example>
        /// <code>
        ///[TestMethod]
        ///public void WhenIncrementingAnInteger()
        ///{
        ///    int x = 0;
        ///    XSpec.Given( "an integer, set to zero", () => x = 0 )
        ///        .When( "the integer is incremented", () => x++ )
        ///            .It( "can be incremented again to two", () => { x++; Assert.AreEqual( x, 2 ); } )
        ///            .It( "should be 1", () => x == 1 )
        ///        .When( "the integer is incremented again", () => x++ )
        ///            .It( "should be 2", () => x == 2 )
        ///        .When( "the integer is divided by zero", () => x = x / ( x - 2 ) )
        ///            .ItShouldThrow&lt;DivideByZeroException>()
        ///    .Go();
        ///}        
        /// </code>
        /// </example>
        public static XSpec Given( string description, Action action )
        {
            return new XSpec( description, action, null, NodeType.Given );
        }

        /// <summary>
        /// Acts as a stand-in for an action which applies some action to the state described by the specification.
        /// </summary>
        /// <param name="description">A string describing the action that is taken. This should be a short sentence
        /// that starts with 'when ...', but eliding the word 'when'. For example, "the moon hits your eye like a big
        /// pizza pie" would be an acceptable value.</param>
        /// <returns>A new specification, which can be further enhanced with additional <see cref="When"/> or 
        /// <see cref="It"/> calls.</returns>
        /// <remarks><para>This overload of the When function always fails with a 
        /// <see cref="NotImplementedException"/>. Use this overload when you're writing your test, but do not yet
        /// have the code which implements the described action.</para>
        /// <para>The <see cref="description"/> parameter should be a short sentence that starts with 'When ...', 
        /// but without the word 'When'. The first reason for this is that the code should be legible. e.g., this:</para>
        /// <code>
        ///   .When( "you make a call to the service", () => service.Call() )
        /// </code>
        /// <para>reads like "When you make a call to the service..."</para>
        /// <para>The second reason is that the string is printed out as part of the test results, and the word "When"
        /// is prepended to the description.</para>
        /// <para>All calls to <c>When</c> are cumulative, that is, each call to <c>When</c> is done in the 
        /// context of the previous calls. To completely reset the state, you should start over again with another 
        /// spec, and another <c>Given</c> call.</para></remarks>
        /// <example>
        /// <code>
        ///[TestMethod]
        ///public void WhenIncrementingAnInteger()
        ///{
        ///    int x = 0;
        ///    XSpec.Given( "an integer, set to zero", () => x = 0 )
        ///        .When( "the integer is incremented", () => x++ )
        ///            .It( "can be incremented again to two", () => { x++; Assert.AreEqual( x, 2 ); } )
        ///            .It( "should be 1", () => x == 1 )
        ///        .When( "the integer is incremented again", () => x++ )
        ///            .It( "should be 2", () => x == 2 )
        ///        .When( "the integer is divided by zero", () => x = x / ( x - 2 ) )
        ///            .ItShouldThrow&lt;DivideByZeroException>()
        ///    .Go();
        ///}        
        /// </code>
        /// </example>
        public XSpec When( string description )
        {
            if ( String.IsNullOrEmpty( description ) ) { throw new ArgumentNullException( "description" ); }

            return When(
                description, () => { throw new NotImplementedException( "This action not yet implemented" ); } );
        }

        /// <summary>
        /// Applies some action to the state described by the specification.
        /// </summary>
        /// <param name="description">A string describing the action that is taken. This should be a short sentence
        /// that starts with 'when ...', but eliding the word 'when'. For example, "the moon hits your eye like a big
        /// pizza pie" would be an acceptable value.</param>
        /// <param name="action">The action to take.</param>
        /// <returns>A new specification, which can be further enhanced with additional <see cref="When"/> or 
        /// <see cref="It"/> calls.</returns>
        /// <remarks>The <see cref="description"/> parameter should be a short sentence that starts with 'When ...', 
        /// but without the word 'When'. The first reason for this is that the code should be legible. e.g., this:
        /// <code>
        ///   .When( "you make a call to the service", () => service.Call() )
        /// </code>
        /// <para>reads like "When you make a call to the service..."</para>
        /// <para>The second reason is that the string is printed out as part of the test results, and the word "When"
        /// is prepended to the description.</para>
        /// <para>All calls to <c>When</c> are cumulative, that is, each call to <c>When</c> is done in the 
        /// context of the previous calls. To completely reset the state, you should start over again with another 
        /// spec, and another <c>Given</c> call.</para></remarks>
        /// <example>
        /// <code>
        ///[TestMethod]
        ///public void WhenIncrementingAnInteger()
        ///{
        ///    int x = 0;
        ///    XSpec.Given( "an integer, set to zero", () => x = 0 )
        ///        .When( "the integer is incremented", () => x++ )
        ///            .It( "can be incremented again to two", () => { x++; Assert.AreEqual( x, 2 ); } )
        ///            .It( "should be 1", () => x == 1 )
        ///        .When( "the integer is incremented again", () => x++ )
        ///            .It( "should be 2", () => x == 2 )
        ///        .When( "the integer is divided by zero", () => x = x / ( x - 2 ) )
        ///            .ItShouldThrow&lt;DivideByZeroException>()
        ///    .Go();
        ///}        
        /// </code>
        /// </example>
        public XSpec When( string description, Action action )
        {
            if ( String.IsNullOrEmpty( description ) ) { throw new ArgumentNullException( "description" ); }
            if ( action == null ) { throw new ArgumentNullException( "action" ); }

            return AddNode( description, action, NodeType.When );
        }

        /// <summary>
        /// Acts as a stand-in for an assertion about the state of the world.
        /// </summary>
        /// <param name="description">A string describing the assertion being made. This should be a short sentence
        /// that starts with 'it ...', but eliding the 'it'. For example, "puts the lotion in the bucket" would be an
        /// acceptable value.</param>
        /// <returns>A new specification, which can have further actions taken in it.</returns>
        /// <remarks><para>This version of the 'It' method generates an assertion which always fails. This overload
        /// should be used when you need a stand-in for an assertion, but haven't written the assertion yet.</para>
        /// <para>The <see cref="description"/> parameter should be a short sentence that starts with 'It ...', 
        /// but without the word 'It'. The first reason for this is that the code should be legible. e.g., this:</para>
        /// <code>
        ///   .It( "makes you think", () => Assert.IsTrue(self.Thinking) )
        /// </code>
        /// <para>reads like "It makes you think"</para>
        /// <para>The second reason is that the string is printed out as part of the test results, and the word "It"
        /// is prepended to the description.</para>
        /// </remarks>
        /// <example>
        /// <code>
        ///[TestMethod]
        ///public void WhenIncrementingAnInteger()
        ///{
        ///    int x = 0;
        ///    XSpec.Given( "an integer, set to zero", () => x = 0 )
        ///        .When( "the integer is incremented", () => x++ )
        ///            .It( "can be incremented again to two", () => { x++; Assert.AreEqual( x, 2 ); } )
        ///            .It( "should be 1", () => x == 1 )
        ///        .When( "the integer is incremented again", () => x++ )
        ///            .It( "should be 2", () => x == 2 )
        ///        .When( "the integer is divided by zero", () => x = x / ( x - 2 ) )
        ///            .ItShouldThrow&lt;DivideByZeroException>()
        ///    .Go();
        ///}        
        /// </code>
        /// </example>
        public XSpec It( string description )
        {
            if ( description == null ) { throw new ArgumentNullException( "description" ); }

            return It(
                description,
                (Action)( () => { throw new NotImplementedException( "This assertion not yet implemented" ); } ) );
        }

        /// <summary>
        /// Makes an assertion about the state of the world.
        /// </summary>
        /// <param name="description">A string describing the assertion being made. This should be a short sentence
        /// that starts with 'it ...', but eliding the 'it'. For example, "puts the lotion in the bucket" would be an
        /// acceptable value.</param>
        /// <param name="assertion">The assertion to be made. It should throw an <see cref="AssertionFailedException"/> 
        /// if it fails.</param>
        /// <returns>A new specification, which can have further actions taken in it.</returns>
        /// <remarks>The <see cref="description"/> parameter should be a short sentence that starts with 'It ...', 
        /// but without the word 'It'. The first reason for this is that the code should be legible. e.g., this:
        /// <code>
        ///   .It( "makes you think", () => Assert.IsTrue(self.Thinking) )
        /// </code>
        /// <para>reads like "It makes you think"</para>
        /// <para>The second reason is that the string is printed out as part of the test results, and the word "It"
        /// is prepended to the description.</para></remarks>
        /// <example>
        /// <code>
        ///[TestMethod]
        ///public void WhenIncrementingAnInteger()
        ///{
        ///    int x = 0;
        ///    XSpec.Given( "an integer, set to zero", () => x = 0 )
        ///        .When( "the integer is incremented", () => x++ )
        ///            .It( "can be incremented again to two", () => { x++; Assert.AreEqual( x, 2 ); } )
        ///            .It( "should be 1", () => x == 1 )
        ///        .When( "the integer is incremented again", () => x++ )
        ///            .It( "should be 2", () => x == 2 )
        ///        .When( "the integer is divided by zero", () => x = x / ( x - 2 ) )
        ///            .ItShouldThrow&lt;DivideByZeroException>()
        ///    .Go();
        ///}        
        /// </code>
        /// </example>
        public XSpec It( string description, Action assertion )
        {
            if ( description == null ) { throw new ArgumentNullException( "description" ); }
            if ( assertion == null ) { throw new ArgumentNullException( "action" ); }

            return AddNode( description, assertion, NodeType.It );
        }

        /// <summary>
        /// Makes an assertion about the state of the world.
        /// </summary>
        /// <param name="description">A string describing the assertion being made. This should be a short sentence
        /// that starts with 'it ...', but eliding the 'it'. For example, "puts the lotion in the bucket" would be an
        /// acceptable value.</param>
        /// <param name="assertion">The predicate to be evaluated. If it returns false, the assertion has failed.
        /// </param>
        /// <returns>A new specification, which can have further actions taken in it.</returns>
        /// <remarks>The <see cref="description"/> parameter should be a short sentence that starts with 'It ...', 
        /// but without the word 'It'. The first reason for this is that the code should be legible. e.g., this:
        /// <code>
        ///   .It( "makes you think", () => Assert.IsTrue(self.Thinking) )
        /// </code>
        /// <para>reads like "It makes you think"</para>
        /// <para>The second reason is that the string is printed out as part of the test results, and the word "It"
        /// is prepended to the description.</para>
        /// <para>The expression is analyzed, a little bit, in order to provide better assertion messages in the case
        /// of failure. In particular, if you write an '==' expression, the assertion is converted into an 
        /// Assert.Equals call, and the right-hand side of the '==' expression is considered the 'expected' value.
        /// </para></remarks>
        /// <example>
        /// <code>
        ///[TestMethod]
        ///public void WhenIncrementingAnInteger()
        ///{
        ///    int x = 0;
        ///    XSpec.Given( "an integer, set to zero", () => x = 0 )
        ///        .When( "the integer is incremented", () => x++ )
        ///            .It( "can be incremented again to two", () => { x++; Assert.AreEqual( x, 2 ); } )
        ///            .It( "should be 1", () => x == 1 )
        ///        .When( "the integer is incremented again", () => x++ )
        ///            .It( "should be 2", () => x == 2 )
        ///        .When( "the integer is divided by zero", () => x = x / ( x - 2 ) )
        ///            .ItShouldThrow&lt;DivideByZeroException>()
        ///    .Go();
        ///}        
        /// </code>
        /// </example>
        public XSpec It( string description, Expression<Func<bool>> assertion )
        {
            if ( description == null ) { throw new ArgumentNullException( "description" ); }
            if ( assertion == null ) { throw new ArgumentNullException( "predicate" ); }

            Action actualAction = null;
            if ( assertion.Body.NodeType == ExpressionType.Equal )
            {
                var be = (BinaryExpression)assertion.Body;

                var leftLambda = (Func<object>)Expression.Lambda( Expression.Convert( be.Left, typeof( object ) ) ).Compile();
                var rightLambda = (Func<object>)Expression.Lambda( Expression.Convert( be.Right, typeof( object ) ) ).Compile();

                if ( be.Right.NodeType == ExpressionType.Constant )
                {
                    var ce = (ConstantExpression)be.Right;
                    if ( ce.Value == null )
                    {
                        actualAction = () => Assert.IsNull( leftLambda() );
                    }
                }
                else if ( be.Left.NodeType == ExpressionType.Constant )
                {
                    var ce = (ConstantExpression)be.Left;
                    if ( ce.Value == null )
                    {
                        actualAction = () => Assert.IsNull( rightLambda() );
                    }
                }

                if ( actualAction == null )
                {
                    actualAction = () => Assert.AreEqual( rightLambda(), leftLambda() );
                }
            }
            else if ( assertion.Body.NodeType == ExpressionType.NotEqual )
            {
                var be = (BinaryExpression)assertion.Body;

                var leftLambda = (Func<object>)Expression.Lambda( Expression.Convert( be.Left, typeof( object ) ) ).Compile();
                var rightLambda = (Func<object>)Expression.Lambda( Expression.Convert( be.Right, typeof( object ) ) ).Compile();

                if ( be.Right.NodeType == ExpressionType.Constant )
                {
                    var ce = (ConstantExpression)be.Right;
                    if ( ce.Value == null )
                    {
                        actualAction = () => Assert.IsNotNull( leftLambda() );
                    }
                }
                else if ( be.Left.NodeType == ExpressionType.Constant )
                {
                    var ce = (ConstantExpression)be.Left;
                    if ( ce.Value == null )
                    {
                        actualAction = () => Assert.IsNotNull( rightLambda() );
                    }
                }

                if ( actualAction == null )
                {
                    actualAction = () => Assert.AreNotEqual( rightLambda(), leftLambda() );
                }
            }
            else
            {
                var compiled = assertion.Compile();
                actualAction = () => Assert.IsTrue( compiled() );
            }

            return AddNode( description, actualAction, NodeType.It );
        }

        /// <summary>
        /// Asserts that the previous action should throw an exception of the specified type.
        /// </summary>
        /// <typeparam name="TException">The type of exception that should have been thrown.</typeparam>
        /// <returns>A spec under which additional actions and assertions can be made.</returns>
        /// <remarks>This assertion has the side effect of making the preceeding action no longer fail when an 
        /// exception is thrown; instead, the exception will be recorded and this assertion will make sure it was of
        /// the specified type.</remarks>
        /// <example>
        /// <code>
        ///[TestMethod]
        ///public void WhenIncrementingAnInteger()
        ///{
        ///    int x = 0;
        ///    XSpec.Given( "an integer, set to zero", () => x = 0 )
        ///        .When( "the integer is incremented", () => x++ )
        ///            .It( "can be incremented again to two", () => { x++; Assert.AreEqual( x, 2 ); } )
        ///            .It( "should be 1", () => x == 1 )
        ///        .When( "the integer is incremented again", () => x++ )
        ///            .It( "should be 2", () => x == 2 )
        ///        .When( "the integer is divided by zero", () => x = x / ( x - 2 ) )
        ///            .ItShouldThrow&lt;DivideByZeroException>()
        ///    .Go();
        ///}        
        /// </code>
        /// </example>
        public XSpec ItShouldThrow<TException>()
        {
            XSpec parent = null;

            XSpec newNode = AddNode(
                "should throw " + typeof( TException ).Name,
                () =>
                {
                    if ( parent.exception == null )
                    {
                        throw new AssertFailedException( "No exception was thrown." );
                    }
                    Exception exception = parent.exception;
                    AggregateException aggregate = parent.exception as AggregateException;
                    if ( aggregate != null )
                    {
                        exception = aggregate.Flatten().InnerException;
                    }
                    Assert.IsInstanceOfType( exception, typeof( TException ) );
                },
                NodeType.It );

            parent = newNode.parent;
            parent.swallowExceptions = true;

            return newNode;
        }

        IEnumerable<XSpec[]> GatherTests( List<XSpec> stack )
        {
            stack.Add( this );

            if ( this.type >= NodeType.It )
            {
                yield return stack.ToArray();
                stack.RemoveAt( stack.Count - 1 );
            }

            foreach ( XSpec child in this.children )
            {
                foreach ( XSpec[] test in child.GatherTests( stack ) ) { yield return test; }
            }
        }

        IEnumerable<XSpec> GatherTestsQuick()
        {
            yield return this;

            foreach ( XSpec child in this.children )
            {
                foreach ( XSpec test in child.GatherTestsQuick() ) { yield return test; }
            }
        }

        /// <summary>
        /// Executes the specification.
        /// </summary>
        /// <exception cref="AssertionFailedException">The specification validation has failed.</exception>
        /// <remarks>
        /// A log of the execution is written to stdout.
        /// <para>The behavior of this method, with respect to isolation, depends on the value of the 
        /// <see cref="DefaultExecutionPolicy"/> property.</para>
        /// </remarks>
        public void Go()
        {
            if ( DefaultExecutionPolicy == SpecExecutionPolicy.Isolated )
            {
                GoIsolated();
            }
            else
            {
                GoQuick();
            }
        }

        /// <summary>
        /// Executes the specification.
        /// </summary>
        /// <exception cref="AssertionFailedException">The specification validation has failed.</exception>
        /// <remarks>A log of the execution is written to stdout.</remarks>
        /// <example>
        /// <code>
        ///[TestMethod]
        ///public void WhenIncrementingAnInteger()
        ///{
        ///    int x = 0;
        ///    XSpec.Given( "an integer, set to zero", () => x = 0 )
        ///        .When( "the integer is incremented", () => x++ )
        ///            .It( "can be incremented again to two", () => { x++; Assert.AreEqual( x, 2 ); } )
        ///            .It( "should be 1", () => x == 1 )
        ///        .When( "the integer is incremented again", () => x++ )
        ///            .It( "should be 2", () => x == 2 )
        ///        .When( "the integer is divided by zero", () => x = x / ( x - 2 ) )
        ///            .ItShouldThrow&lt;DivideByZeroException>()
        ///    .Go();
        ///}        
        /// </code>
        /// </example>
        public void GoIsolated()
        {
            XSpec root = this;
            while ( root.parent != null ) { root = root.parent; }
            Run( root );
        }

        /// <summary>
        /// Executes the specification, in a less-isolated fashion.
        /// </summary>
        /// <exception cref="AssertionFailedException">The specification validation has failed.</exception>
        /// <remarks>A log of the execution is written to stdout.
        /// <para>This is a much faster way to run tests than <see cref="GoIsolated"/>; instead of isolating each
        /// assertion, this execution mode runs each step until the first failure; it then backs up and runs everything
        /// up to the first failure, skipping any assertions along the way. The upshot is that if an assertion fails,
        /// we re-run the <see cref="Given"/>s and <see cref="Where"/>s to re-establish context, and then continue on.
        /// </para>
        /// <para>This has the quadratic run time when all the assertions fail, but a linear run time when they all
        /// pass, which is most of the time.</para>
        /// </remarks>
        public void GoQuick()
        {
            XSpec root = this;
            while ( root.parent != null ) { root = root.parent; }
            RunQuick( root );
        }

        bool Report( StringBuilder report, int depth )
        {
            StringBuilder builder = new StringBuilder();
            for ( int i = 0; i < depth; i++ )
            {
                builder.Append( "  " );
            }

            if ( this.type == NodeType.TheException )
            {
                builder.Append( "The exception " );
            }
            else
            {
                builder.Append( this.type.ToString() );
                builder.Append( " " );
            }

            builder.Append( this.description );
            builder.Append( " (" );

            string message = null;
            bool multiMessage = false;
            long averageMs = 0;
            Exception exception = null;
            if ( this.results.Count > 0 )
            {
                long totalMs = 0;
                for ( int i = 0; i < this.results.Count; i++ )
                {
                    totalMs += this.results[i].Item1;
                    if ( this.results[i].Item2 != null )
                    {
                        if ( exception == null )
                        {
                            exception = this.results[i].Item2;
                        }

                        if ( message == null )
                        {
                            message = this.results[i].Item2.Message;
                        }
                        else if ( !String.Equals( message, this.results[i].Item2.Message ) )
                        {
                            multiMessage = true;
                        }
                    }
                }
                averageMs = totalMs / this.results.Count;
            }
            builder.AppendFormat( "{0}ms, ", averageMs );

            if ( multiMessage ) { builder.Append( "+" ); }
            switch ( this.state )
            {
            case ExecState.Exception: builder.Append( "exception" ); break;
            case ExecState.Failed: builder.Append( "FAILED" ); break;
            case ExecState.NotRun: builder.Append( "not run" ); break;
            case ExecState.Passed: builder.Append( "ok" ); break;
            }
            if ( message != null )
            {
                builder.AppendFormat( ": '{0}'", message );
            }
            builder.AppendLine( ")" );

            // Add the line we're building to the overall report
            report.Append( builder.ToString() );

            // Dump the line we're building to stdout
            Console.Write( builder.ToString() );
            if ( exception != null )
            {
                // The console also gets exception details.
                using ( var writer = new IndentedTextWriter( Console.Out, "  " ) )
                {
                    writer.Indent = depth;
                    writer.WriteLine( exception.ToString() );
                }
            }

            bool result = true;
            for ( int i = 0; i < this.children.Count; i++ )
            {
                result = this.children[i].Report( report, depth + 1 ) && result;
            }

            return result && ( this.state == ExecState.Passed );
        }

        static void Run( XSpec root )
        {
            var stack = new List<XSpec>();
            List<XSpec[]> tests = root.GatherTests( stack ).ToList();
            for ( int i = 0; i < tests.Count; i++ )
            {
                XSpec[] test = tests[i];
                for ( int j = 0; j < test.Length; j++ )
                {
                    if ( !test[j].Exec() ) { break; }
                }
            }

            var builder = new StringBuilder();
            bool passed = root.Report( builder, 0 );

            if ( !passed )
            {
                throw new AssertFailedException( "\r\n" + builder.ToString() );
            }
        }

        static void RunQuick( XSpec root )
        {
            XSpec[] tests = root.GatherTestsQuick().ToArray();
            int end = 0;
            while ( end < tests.Length )
            {
                // First, fast-forward, resetting state until we get up to just past our last failure.
                bool failedPrereqs = false;
                for ( int i = 0; i < end; i++ )
                {
                    if ( tests[i].type > NodeType.When ) { continue; }
                    if ( !tests[i].Exec() )
                    {
                        failedPrereqs = true;
                        break;
                    }
                }

                if ( failedPrereqs ) { break; }
                for ( ; end < tests.Length; end++ )
                {
                    if ( !tests[end].Exec() ) { break; }
                }
                end++; // Skip forward, so we can make progress.
            }

            var builder = new StringBuilder();
            bool passed = root.Report( builder, 0 );

            if ( !passed )
            {
                throw new AssertFailedException( "\r\n" + builder.ToString() );
            }
        }

        /// <summary>
        /// Makes an assertion about an exception raised by the previous action.
        /// </summary>
        /// <param name="description">A string describing the assertion being made. This should be a short sentence
        /// that starts with 'it ...', but eliding the 'it'. For example, "puts the lotion in the bucket" would be an
        /// acceptable value.</param>
        /// <param name="assertion">The assertion to be made. It should throw an <see cref="AssertionFailedException"/> 
        /// if it fails.</param>
        /// <returns>A spec under which additional actions and assertions can be made.</returns>
        /// <remarks>This assertion has the side effect of making the preceeding action no longer fail when an 
        /// exception is thrown.</remarks> 
        public XSpec TheException( string description, Action<Exception> assertion )
        {
            if ( description == null ) { throw new ArgumentNullException( "description" ); }
            if ( assertion == null ) { throw new ArgumentNullException( "assertion" ); }

            XSpec parent = null;
            XSpec newNode = AddNode(
                description,
                () =>
                {
                    if ( parent.exception == null ) { throw new AssertFailedException( "No exception was thrown." ); }
                    assertion( parent.exception );
                },
                NodeType.TheException, NodeType.It );

            parent = newNode.parent;
            parent.swallowExceptions = true;

            return newNode;
        }

        /// <summary>
        /// Makes an assertion about an exception raised by the previous action.
        /// </summary>
        /// <param name="description">A string describing the assertion being made. This should be a short sentence
        /// that starts with 'it ...', but eliding the 'it'. For example, "puts the lotion in the bucket" would be an
        /// acceptable value.</param>
        /// <param name="assertion">The assertion to be made. It should throw an <see cref="AssertionFailedException"/> 
        /// if it fails.</param>
        /// <returns>A spec under which additional actions and assertions can be made.</returns>
        /// <remarks>This assertion has the side effect of making the preceeding action no longer fail when an 
        /// exception is thrown.</remarks> 
        public XSpec TheException( string description, Func<Exception, bool> assertion )
        {
            if ( description == null ) { throw new ArgumentNullException( "description" ); }
            if ( assertion == null ) { throw new ArgumentNullException( "assertion" ); }

            XSpec parent = null;
            XSpec newNode = AddNode(
                description,
                () =>
                {
                    if ( parent.exception == null ) { throw new AssertFailedException( "No exception was thrown." ); }
                    Assert.IsTrue( assertion( parent.exception ) );
                },
                NodeType.TheException, NodeType.It );

            parent = newNode.parent;
            parent.swallowExceptions = true;

            return newNode;
        }

        /// <summary>
        /// The executed state of a spec node. The ordering is used for precedence; higher values take precedence over 
        /// lower ones.
        /// </summary>
        enum ExecState
        {
            NotRun = 0,
            Passed = 1,
            Failed = 2,
            Exception = 3,
        }

        /// <summary>
        /// The type of a node; they're ordered so that we can get nesting and ordering right.
        /// </summary>
        enum NodeType
        {
            Given = 0,
            When = 1,
            It = 2,
            TheException = 3,
        }
    }

    /// <summary>
    /// Indicates which policy should be used to run a given specification.
    /// </summary>
    public enum SpecExecutionPolicy
    {
        /// <summary>
        /// The tests should be run in quick mode, ala <see cref="XSpec.GoQuick"/>
        /// </summary>
        Quick,

        /// <summary>
        /// The tests should be run in isolated mode, ala <see cref="XSpec.GoIsolated"/>
        /// </summary>        
        Isolated
    }
}
