//-----------------------------------------------------------------------
// <copyright file="DynamicRavenQueryProvider.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;
using System.Reflection;
using Raven.Abstractions.Data;
#if !NET_3_5
using Raven.Client.Connection.Async;
#endif
using Raven.Client.Connection;
using Raven.Client.Document;

namespace Raven.Client.Linq
{
	/// <summary>
	/// This is a specialized query provider for querying dynamic indexes
	/// </summary>
	/// <typeparam name="T"></typeparam>
	public class DynamicRavenQueryProvider<T> : IRavenQueryProvider
	{
		private Action<IDocumentQueryCustomization> customizeQuery;
		private Action<QueryResult> afterQueryExecuted;
		private readonly IDocumentQueryGenerator queryGenerator;
		private readonly RavenQueryStatistics ravenQueryStatistics;
#if !SILVERLIGHT
		private readonly IDatabaseCommands databaseCommands;
#endif
#if !NET_3_5
		private readonly IAsyncDatabaseCommands asyncDatabaseCommands;
#endif
		private readonly string indexName;

		/// <summary>
		/// Gets the IndexName for this dynamic query provider
		/// </summary>
		public string IndexName
		{
			get { return indexName; }
		}

		/// <summary>
		/// Get the query generator
		/// </summary>
		public IDocumentQueryGenerator QueryGenerator
		{
			get { return queryGenerator; }
		}

		/// <summary>
		/// Creates a dynamic query provider around the provided document session
		/// </summary>
		public DynamicRavenQueryProvider(
			IDocumentQueryGenerator queryGenerator, 
			string indexName, 
			RavenQueryStatistics ravenQueryStatistics
#if !SILVERLIGHT
			,IDatabaseCommands databaseCommands
#endif
#if !NET_3_5
			,IAsyncDatabaseCommands asyncDatabaseCommands
#endif
			)
		{
			FieldsToFetch = new HashSet<string>();
			FieldsToRename = new Dictionary<string, string>();
			this.queryGenerator = queryGenerator;
			this.indexName = indexName;
			this.ravenQueryStatistics = ravenQueryStatistics;
#if !SILVERLIGHT
			this.databaseCommands = databaseCommands;
#endif
#if !NET_3_5
			this.asyncDatabaseCommands = asyncDatabaseCommands;
#endif
		}

		/// <summary>
		/// Gets the actions for customizing the generated lucene query
		/// </summary>
		public Action<IDocumentQueryCustomization> CustomizedQuery
		{
			get { return customizeQuery; }
		}


		/// <summary>
		/// Change the result type for the query provider
		/// </summary>
		public IRavenQueryProvider For<S>()
		{
			if (typeof(T) == typeof(S))
				return this;

			var ravenQueryProvider = new DynamicRavenQueryProvider<S>(queryGenerator, indexName, ravenQueryStatistics 
#if !SILVERLIGHT
				,databaseCommands
#endif
#if !NET_3_5
				,asyncDatabaseCommands
#endif
			);
			ravenQueryProvider.Customize(customizeQuery);
			return ravenQueryProvider;
		}

		/// <summary>
		/// Set the fields to fetch
		/// </summary>
		public HashSet<string> FieldsToFetch { get; private set; }

		/// <summary>
		/// Set the fields to rename
		/// </summary>
		public Dictionary<string, string> FieldsToRename { get; private set; }

		/// <summary>
		/// Convert the expression to a Lucene query
		/// </summary>
		public IDocumentQuery<TResult> ToLuceneQuery<TResult>(Expression expression)
		{
			var processor = GetQueryProviderProcessor<T>();
			return (IDocumentQuery<TResult>)processor.GetLuceneQueryFor(expression);
		}

#if !NET_3_5

		/// <summary>
		/// Move the registered after query actions
		/// </summary>
		public void MoveAfterQueryExecuted<K>(IAsyncDocumentQuery<K> documentQuery)
		{
			if (afterQueryExecuted != null)
				documentQuery.AfterQueryExecuted(afterQueryExecuted);
		}

		/// <summary>
		/// Convert the expression to a Lucene query
		/// </summary>
		public IAsyncDocumentQuery<TResult> ToAsyncLuceneQuery<TResult>(Expression expression)
		{
			var processor = GetQueryProviderProcessor<T>();
			return (IAsyncDocumentQuery<TResult>)processor.GetAsyncLuceneQueryFor(expression);
		}

		public Lazy<IEnumerable<S>> Lazily<S>(Expression expression, Action<IEnumerable<S>> onEval)
		{
			var processor = GetQueryProviderProcessor<S>();
			var query = processor.GetLuceneQueryFor(expression);
			if(afterQueryExecuted != null)
				query.AfterQueryExecuted(afterQueryExecuted);
			if (FieldsToFetch.Count > 0)
				query = query.SelectFields<S>(FieldsToFetch.ToArray());
			return query.Lazily(onEval);
		}

#endif

		/// <summary>
		/// Executes the query represented by a specified expression tree.
		/// </summary>
		/// <param name="expression">An expression tree that represents a LINQ query.</param>
		/// <returns>
		/// The value that results from executing the specified query.
		/// </returns>
		public virtual object Execute(Expression expression)
		{
			return GetQueryProviderProcessor<T>().Execute(expression);
		}

		DynamicQueryProviderProcessor<S> GetQueryProviderProcessor<S>()
		{
			return new DynamicQueryProviderProcessor<S>(queryGenerator, customizeQuery, afterQueryExecuted, indexName, FieldsToFetch, FieldsToRename);
		}

		IQueryable<S> IQueryProvider.CreateQuery<S>(Expression expression)
		{
			return new DynamicRavenQueryInspector<S>(this, ravenQueryStatistics, indexName, expression
#if !SILVERLIGHT
				, databaseCommands
#endif
#if !NET_3_5
				, asyncDatabaseCommands
#endif
			);
		}

		IQueryable IQueryProvider.CreateQuery(Expression expression)
		{
			Type elementType = TypeSystem.GetElementType(expression.Type);
			try
			{
				return
					(IQueryable)
					Activator.CreateInstance(typeof(DynamicRavenQueryInspector<>).MakeGenericType(elementType),
											 new object[] { this, ravenQueryStatistics, indexName, expression
#if !SILVERLIGHT
												 ,databaseCommands
#endif
#if !NET_3_5
												 ,asyncDatabaseCommands
#endif
												 });
			}
			catch (TargetInvocationException tie)
			{
				throw tie.InnerException;
			}
		}

		/// <summary>
		/// Executes the specified expression.
		/// </summary>
		/// <typeparam name="S"></typeparam>
		/// <param name="expression">The expression.</param>
		/// <returns></returns>
		S IQueryProvider.Execute<S>(Expression expression)
		{
			return (S)Execute(expression);
		}

		/// <summary>
		/// Executes the query represented by a specified expression tree.
		/// </summary>
		/// <param name="expression">An expression tree that represents a LINQ query.</param>
		/// <returns>
		/// The value that results from executing the specified query.
		/// </returns>
		object IQueryProvider.Execute(Expression expression)
		{
			return Execute(expression);
		}

		/// <summary>
		/// Callback to get the results of the query
		/// </summary>
		public void AfterQueryExecuted(Action<QueryResult> afterQueryExecutedCallback)
		{
			this.afterQueryExecuted = afterQueryExecutedCallback;
		}


		/// <summary>
		/// Customizes the query using the specified action
		/// </summary>
		/// <param name="action">The action.</param>
		public virtual void Customize(Action<IDocumentQueryCustomization> action)
		{
			if (action == null)
				return;
			customizeQuery += action;
		}
	}
}
