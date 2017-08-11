using Impatient.Query;
using Impatient.Query.Expressions;
using Impatient.Query.ExpressionVisitors;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace Impatient.Tests
{
	public class Queryable<T>
	{
		private Expression _expression;
		private ImpatientQueryProvider _impatient;

		public Queryable(ImpatientQueryProvider impatient, params Expression<Func<T, object>>[] props)
		{
			_impatient = impatient;
			_expression = CreateExpression(props);
		}

		private static string GetMemberName<TItem>(Expression<Func<TItem, object>> func)
		{
			var mem = func as MemberExpression;
			return mem.Member.Name;
		}

		public static Expression CreateExpression(params Expression<Func<T, object>>[] props)
		{
			var type = typeof(T);
			var propertyNames = props?.Select(GetMemberName).ToList();
			if (propertyNames?.Any() != true)
			{
				propertyNames = type.GetProperties()
					.Where(x => x.PropertyType.IsScalarType())
					.Select(x => x.Name).ToList();
			}

			var myClass1Table = new BaseTableExpression("dbo", type.Name, type.Name, typeof(T));
			var expression = new EnumerableRelationalQueryExpression(
							new SelectExpression(
									new ServerProjectionExpression(
											Expression.MemberInit(
													Expression.New(type),
													from propName in propertyNames
													let property = type.GetRuntimeProperty(propName)
													let column = new SqlColumnExpression(myClass1Table, property.Name, property.PropertyType)
													select Expression.Bind(property, column))),
									myClass1Table));
			return expression;
		}

		public IQueryable<T> Query() => _impatient.CreateQuery<T>(_expression);
	}


	[TestClass]
	public class QueryTests
	{
		private string SqlLog => commandLog.ToString();

		private readonly StringBuilder commandLog = new StringBuilder();

		private readonly ImpatientQueryProvider impatient;

		private Queryable<MyClass1> MyClass1Query;

		private Queryable<MyClass2> MyClass2Query;


		public QueryTests()
		{
			impatient = new ImpatientQueryProvider(
					new TestImpatientConnectionFactory(),
					new DefaultImpatientQueryCache(),
					new DefaultImpatientExpressionVisitorProvider())
			{
				DbCommandInterceptor = command =>
				{
					if (commandLog.Length > 0)
					{
						commandLog.AppendLine().AppendLine();
					}

					commandLog.Append(command.CommandText);
				}
			};

			MyClass1Query = new Queryable<MyClass1>(impatient, x => x.Prop1, x => x.Prop2);
			MyClass2Query = new Queryable<MyClass2>(impatient);
		}

		[TestCleanup]
		public void Cleanup()
		{
			commandLog.Clear();
		}

		[TestMethod]
		public void Select_parameterized_from_closure()
		{
			var localVariable = 77;

			var query1 =
					from a in MyClass1Query.Query()
					where a.Prop2 == localVariable
					select a;

			var results1 = query1.ToList();

			Assert.AreEqual(1, results1.Count);

			localVariable = 9;

			var query2 =
					from a in MyClass1Query.Query()
					where a.Prop2 == localVariable
					select a;

			var results2 = query1.ToList();

			Assert.AreEqual(1, results2.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE [a].[Prop2] = @p0

SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE [a].[Prop2] = @p0",
					SqlLog);
		}

		private int instanceField = 77;

		[TestMethod]
		public void Select_parameterized_from_constant_this()
		{
			var query1 =
					from a in MyClass1Query.Query()
					where a.Prop2 == instanceField
					select a;

			var results1 = query1.ToList();

			Assert.AreEqual(1, results1.Count);

			instanceField = 9;

			var query2 =
					from a in MyClass1Query.Query()
					where a.Prop2 == instanceField
					select a;

			var results2 = query1.ToList();

			Assert.AreEqual(1, results2.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE [a].[Prop2] = @p0

SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE [a].[Prop2] = @p0",
					SqlLog);
		}

		[TestMethod]
		public void SelectMany_Where()
		{
			var query =
					from a in MyClass1Query.Query()
					from b in MyClass2Query.Query()
					where a.Prop1 == b.Prop1
					select a;

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
CROSS JOIN [dbo].[MyClass2] AS [m]
WHERE [a].[Prop1] = [m].[Prop1]",
					SqlLog);
		}

		[TestMethod]
		public void SelectMany_with_second_source_from_closure()
		{
			var set2 = MyClass2Query.Query();

			var query =
					from a in MyClass1Query.Query()
					from b in set2
					where a.Prop1 == b.Prop1
					select a;

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
CROSS JOIN [dbo].[MyClass2] AS [m]
WHERE [a].[Prop1] = [m].[Prop1]",
					SqlLog);
		}

		[TestMethod]
		public void SelectMany_Where_Correlated()
		{
			var query =
					from a in MyClass1Query.Query()
					from b in MyClass2Query.Query().Where(b => a.Prop1 == b.Prop1)
					select a;

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
CROSS APPLY (
    SELECT [b].[Prop1] AS [Prop1], [b].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass2] AS [b]
    WHERE [a].[Prop1] = [b].[Prop1]
) AS [b_0]",
					SqlLog);
		}

		[TestMethod]
		public void SelectMany_collectionSelector_resultSelector()
		{
			var query
					= MyClass1Query.Query()
							.SelectMany(
									m1 => MyClass2Query.Query(),
                        (m1, m2) => new { m1, m2 });

			query.ToList();

			Assert.AreEqual(
					@"SELECT [m1].[Prop1] AS [m1.Prop1], [m1].[Prop2] AS [m1.Prop2], [m].[Prop1] AS [m2.Prop1], [m].[Prop2] AS [m2.Prop2]
FROM [dbo].[MyClass1] AS [m1]
CROSS JOIN [dbo].[MyClass2] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void SelectMany_collectionSelector_resultSelector_with_index_referenced()
		{
			var query
					= MyClass1Query.Query()
							.OrderBy(m1 => m1.Prop2)
							.SelectMany(
									(m1, i) => MyClass2Query.Query().Select(m2 => new { m2, i }),
                        (m1, x) => new { m1, x.m2, x.i });

			query.ToList();

			Assert.AreEqual(
					@"SELECT [m1].[Prop1] AS [m1.Prop1], [m1].[Prop2] AS [m1.Prop2], [x].[m2.Prop1] AS [m2.Prop1], [x].[m2.Prop2] AS [m2.Prop2], [x].[i] AS [i]
FROM (
    SELECT [m1_0].[Prop1] AS [Prop1], [m1_0].[Prop2] AS [Prop2], CAST(ROW_NUMBER() OVER(ORDER BY [m1_0].[Prop2] ASC) - 1 AS int) AS [$rownumber]
    FROM [dbo].[MyClass1] AS [m1_0]
) AS [m1]
CROSS APPLY (
    SELECT [m2].[Prop1] AS [m2.Prop1], [m2].[Prop2] AS [m2.Prop2], [m1].[$rownumber] AS [i]
    FROM [dbo].[MyClass2] AS [m2]
) AS [x]",
					SqlLog);
		}

		[TestMethod]
		public void SelectMany_collectionSelector_resultSelector_with_index_referenced_no_ordering()
		{
			var query
					= MyClass1Query.Query()
							.SelectMany(
									(m1, i) => MyClass2Query.Query().Select(m2 => new { m2, i }),
                        (m1, x) => new { m1, x.m2, x.i });

			query.ToList();

			Assert.AreEqual(
					@"SELECT [m1].[Prop1] AS [m1.Prop1], [m1].[Prop2] AS [m1.Prop2], [x].[m2.Prop1] AS [m2.Prop1], [x].[m2.Prop2] AS [m2.Prop2], [x].[i] AS [i]
FROM (
    SELECT [m1_0].[Prop1] AS [Prop1], [m1_0].[Prop2] AS [Prop2], CAST(ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) - 1 AS int) AS [$rownumber]
    FROM [dbo].[MyClass1] AS [m1_0]
) AS [m1]
CROSS APPLY (
    SELECT [m2].[Prop1] AS [m2.Prop1], [m2].[Prop2] AS [m2.Prop2], [m1].[$rownumber] AS [i]
    FROM [dbo].[MyClass2] AS [m2]
) AS [x]",
					SqlLog);
		}

		[TestMethod]
		public void SelectMany_collectionSelector_resultSelector_with_index_unreferenced()
		{
			var query
					= MyClass1Query.Query()
							.OrderBy(m1 => m1.Prop2)
							.SelectMany(
									(m1, i) => MyClass2Query.Query(),
                        (m1, m2) => new { m1, m2 });

			query.ToList();

			Assert.AreEqual(
					@"SELECT [m1].[Prop1] AS [m1.Prop1], [m1].[Prop2] AS [m1.Prop2], [m].[Prop1] AS [m2.Prop1], [m].[Prop2] AS [m2.Prop2]
FROM [dbo].[MyClass1] AS [m1]
CROSS JOIN [dbo].[MyClass2] AS [m]
ORDER BY [m1].[Prop2] ASC",
					SqlLog);
		}

		[TestMethod]
		public void SelectMany_selector()
		{
			var query
					= MyClass1Query.Query()
							.SelectMany(m1 => MyClass2Query.Query());

			query.ToList();

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m1]
CROSS JOIN [dbo].[MyClass2] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void SelectMany_selector_with_index_referenced()
		{
			var query
					= MyClass1Query.Query()
							.OrderBy(m1 => m1.Prop2)
							.SelectMany((m1, i) => MyClass2Query.Query().Select(m2 => new { m2, i }));

			query.ToList();

			Assert.AreEqual(
					@"SELECT [t].[m2.Prop1] AS [m2.Prop1], [t].[m2.Prop2] AS [m2.Prop2], [t].[i] AS [i]
FROM (
    SELECT [m1].[Prop1] AS [Prop1], [m1].[Prop2] AS [Prop2], CAST(ROW_NUMBER() OVER(ORDER BY [m1].[Prop2] ASC) - 1 AS int) AS [$rownumber]
    FROM [dbo].[MyClass1] AS [m1]
) AS [m1_0]
CROSS APPLY (
    SELECT [m2].[Prop1] AS [m2.Prop1], [m2].[Prop2] AS [m2.Prop2], [m1_0].[$rownumber] AS [i]
    FROM [dbo].[MyClass2] AS [m2]
) AS [t]",
					SqlLog);
		}

		[TestMethod]
		public void SelectMany_selector_with_index_referenced_no_ordering()
		{
			var query
					= MyClass1Query.Query()
							.SelectMany((m1, i) => MyClass2Query.Query().Select(m2 => new { m2, i }));

			query.ToList();

			Assert.AreEqual(
					@"SELECT [t].[m2.Prop1] AS [m2.Prop1], [t].[m2.Prop2] AS [m2.Prop2], [t].[i] AS [i]
FROM (
    SELECT [m1].[Prop1] AS [Prop1], [m1].[Prop2] AS [Prop2], CAST(ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) - 1 AS int) AS [$rownumber]
    FROM [dbo].[MyClass1] AS [m1]
) AS [m1_0]
CROSS APPLY (
    SELECT [m2].[Prop1] AS [m2.Prop1], [m2].[Prop2] AS [m2.Prop2], [m1_0].[$rownumber] AS [i]
    FROM [dbo].[MyClass2] AS [m2]
) AS [t]",
					SqlLog);
		}

		[TestMethod]
		public void SelectMany_selector_with_index_unreferenced()
		{
			var query
					= MyClass1Query.Query()
							.SelectMany(m1 => MyClass2Query.Query());

			query.ToList();

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m1]
CROSS JOIN [dbo].[MyClass2] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Join_with_simple_property_keys()
		{
			var query =
					from a in MyClass1Query.Query()
					join b in MyClass2Query.Query()
                    on a.Prop1 equals b.Prop1
								select new { a, b };

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [a.Prop1], [a].[Prop2] AS [a.Prop2], [b].[Prop1] AS [b.Prop1], [b].[Prop2] AS [b.Prop2]
FROM [dbo].[MyClass1] AS [a]
INNER JOIN [dbo].[MyClass2] AS [b] ON [a].[Prop1] = [b].[Prop1]",
					SqlLog);
		}

		[TestMethod]
		public void Join_with_complex_property_keys_NewExpression()
		{
			var query =
					from a in MyClass1Query.Query()
					join b in MyClass2Query.Query()
                    on new { a.Prop1, a.Prop2, }

										equals new { b.Prop1, b.Prop2, }

								select new { a, b };

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [a.Prop1], [a].[Prop2] AS [a.Prop2], [b].[Prop1] AS [b.Prop1], [b].[Prop2] AS [b.Prop2]
FROM [dbo].[MyClass1] AS [a]
INNER JOIN [dbo].[MyClass2] AS [b] ON ([a].[Prop1] = [b].[Prop1]) AND ([a].[Prop2] = [b].[Prop2])",
					SqlLog);
		}

		[TestMethod]
		public void Join_with_complex_property_keys_MemberInitExpression()
		{
			var query =
					from a in MyClass1Query.Query()
					join b in MyClass2Query.Query()
                    on new MyKeyObject { Prop1 = a.Prop1, Prop2 = a.Prop2, }

										equals new MyKeyObject { Prop1 = b.Prop1, Prop2 = b.Prop2, }

								select new { a, b };

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [a.Prop1], [a].[Prop2] AS [a.Prop2], [b].[Prop1] AS [b.Prop1], [b].[Prop2] AS [b.Prop2]
FROM [dbo].[MyClass1] AS [a]
INNER JOIN [dbo].[MyClass2] AS [b] ON ([a].[Prop1] = [b].[Prop1]) AND ([a].[Prop2] = [b].[Prop2])",
					SqlLog);
		}

		[TestMethod]
		public void Join_on_Binary_Add()
		{
			var query =
					from a in MyClass1Query.Query()
					join b in MyClass2Query.Query()
                    on a.Prop2 + a.Prop2

										equals b.Prop2 + b.Prop2

								select new { a, b };

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [a.Prop1], [a].[Prop2] AS [a.Prop2], [b].[Prop1] AS [b.Prop1], [b].[Prop2] AS [b.Prop2]
FROM [dbo].[MyClass1] AS [a]
INNER JOIN [dbo].[MyClass2] AS [b] ON ([a].[Prop2] + [a].[Prop2]) = ([b].[Prop2] + [b].[Prop2])",
					SqlLog);
		}

		[TestMethod]
		public void Join_when_inner_has_Take()
		{
			var query =
					from a in MyClass1Query.Query()
					join b in MyClass2Query.Query().Take(1)

										on a.Prop1 equals b.Prop1
								select new { a, b };

			var results = query.ToList();

			Assert.AreEqual(1, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [a.Prop1], [a].[Prop2] AS [a.Prop2], [b].[Prop1] AS [b.Prop1], [b].[Prop2] AS [b.Prop2]
FROM [dbo].[MyClass1] AS [a]
INNER JOIN (
    SELECT TOP (1) [b_0].[Prop1] AS [Prop1], [b_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass2] AS [b_0]
) AS [b] ON [a].[Prop1] = [b].[Prop1]",
					SqlLog);
		}

		[TestMethod]
		public void Join_when_inner_has_Distinct()
		{
			var query =
					from a in MyClass1Query.Query()
					join b in MyClass2Query.Query().Distinct()

										on a.Prop1 equals b.Prop1
								select new { a, b };

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [a.Prop1], [a].[Prop2] AS [a.Prop2], [b].[Prop1] AS [b.Prop1], [b].[Prop2] AS [b.Prop2]
FROM [dbo].[MyClass1] AS [a]
INNER JOIN (
    SELECT DISTINCT [b_0].[Prop1] AS [Prop1], [b_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass2] AS [b_0]
) AS [b] ON [a].[Prop1] = [b].[Prop1]",
					SqlLog);
		}

		[TestMethod]
		public void Join_Where()
		{
			var query =
					from a in MyClass1Query.Query()
					join b in MyClass2Query.Query()
                    on a.Prop1 equals b.Prop1
								where a.Prop2 < 10 || b.Prop2 > 76

								select new { a1 = a.Prop1, b1 = b.Prop1 };

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [a1], [b].[Prop1] AS [b1]
FROM [dbo].[MyClass1] AS [a]
INNER JOIN [dbo].[MyClass2] AS [b] ON [a].[Prop1] = [b].[Prop1]
WHERE ([a].[Prop2] < 10) OR ([b].[Prop2] > 76)",
					SqlLog);
		}

		[TestMethod]
		public void Join_with_Where_on_queries()
		{
			var query =
					from a in MyClass1Query.Query().Where(x => x.Prop2 < 10)
					join b in MyClass2Query.Query().Where(x => x.Prop2 < 10)

										on a.Prop1 equals b.Prop1
								select new { a1 = a.Prop1, b1 = b.Prop1 };

			var results = query.ToList();

			Assert.AreEqual(1, results.Count);

			Assert.AreEqual(
					@"SELECT [x].[Prop1] AS [a1], [b].[Prop1] AS [b1]
FROM [dbo].[MyClass1] AS [x]
INNER JOIN (
    SELECT [x_0].[Prop1] AS [Prop1], [x_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass2] AS [x_0]
    WHERE [x_0].[Prop2] < 10
) AS [b] ON [x].[Prop1] = [b].[Prop1]
WHERE [x].[Prop2] < 10",
					SqlLog);
		}

		[TestMethod]
		public void Join_with_Joined()
		{
			var query =
					from a in MyClass1Query.Query()
					join b in (from a in MyClass1Query.Query()
										 join b in MyClass2Query.Query() on a.Prop1 equals b.Prop1
					select new { l1 = new { l2 = new { l3 = b.Prop1 } } }) on a.Prop1 equals b.l1.l2.l3
		 select new { a1 = a.Prop1, b1 = b.l1.l2.l3 };

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [a1], [b].[l1.l2.l3] AS [b1]
FROM [dbo].[MyClass1] AS [a]
INNER JOIN (
    SELECT [b_0].[Prop1] AS [l1.l2.l3]
    FROM [dbo].[MyClass1] AS [a_0]
    INNER JOIN [dbo].[MyClass2] AS [b_0] ON [a_0].[Prop1] = [b_0].[Prop1]
) AS [b] ON [a].[Prop1] = [b].[l1.l2.l3]",
					SqlLog);
		}

		[TestMethod]
		public void Join_with_complex_lifted_key()
		{
			var query =
					from a in MyClass1Query.Query()
					join b in (from c in MyClass1Query.Query()
										 join d in MyClass2Query.Query() on c.Prop1 equals d.Prop1
					select new { l1 = new { l2 = new { l3 = d.Prop1 } } })
                    on new { l2 = new { l3 = a.Prop1 } } equals b.l1
								select new { a1 = a.Prop1, b1 = b.l1.l2.l3 };

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [a1], [b].[l1.l2.l3] AS [b1]
FROM [dbo].[MyClass1] AS [a]
INNER JOIN (
    SELECT [d].[Prop1] AS [l1.l2.l3]
    FROM [dbo].[MyClass1] AS [c]
    INNER JOIN [dbo].[MyClass2] AS [d] ON [c].[Prop1] = [d].[Prop1]
) AS [b] ON [a].[Prop1] = [b].[l1.l2.l3]",
					SqlLog);
		}

		[TestMethod]
		public void Select_Where()
		{
			var query =
					from a in MyClass1Query.Query()
					select new { x = new { a.Prop1 }, a.Prop2 } into a
					where a.x.Prop1 == "What the"
					select a.Prop2;

			var results = query.ToList();

			Assert.AreEqual(1, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE [a].[Prop1] = N'What the'",
					SqlLog);
		}

		[TestMethod]
		public void Select_Where_bobby_tables()
		{
			var query =
					from a in MyClass1Query.Query()
					select new { x = new { a.Prop1 }, a.Prop2 } into a
					where a.x.Prop1 == "'; DROP TABLE [dbo].[MyClass];--"
					select a.Prop2;

			query.ToList();

			Assert.AreEqual(
					@"SELECT [a].[Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE [a].[Prop1] = N'''; DROP TABLE [dbo].[MyClass];--'",
					SqlLog);
		}

		[TestMethod]
		public void Select_with_index()
		{
			var query
					= MyClass1Query.Query()
							.OrderBy(m => m.Prop2)
							.Select((m, i) => new { m, i });

			var results = query.ToList();

			Assert.AreEqual(0, results.First().i);

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [m.Prop1], [m].[Prop2] AS [m.Prop2], CAST(ROW_NUMBER() OVER(ORDER BY [m].[Prop2] ASC) - 1 AS int) AS [i]
FROM [dbo].[MyClass1] AS [m]
ORDER BY [m].[Prop2] ASC",
					SqlLog);
		}

		[TestMethod]
		public void Select_with_index_no_ordering()
		{
			var query
					= MyClass1Query.Query()
							.Select((m, i) => new { m, i });

			var results = query.ToList();

			Assert.AreEqual(0, results.First().i);

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [m.Prop1], [m].[Prop2] AS [m.Prop2], CAST(ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) - 1 AS int) AS [i]
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Select_Binary_Add()
		{
			var query =
					from a in MyClass1Query.Query()
					select new { x = a.Prop2 + a.Prop2 };

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop2] + [a].[Prop2] AS [x]
FROM [dbo].[MyClass1] AS [a]",
					SqlLog);
		}

		[TestMethod]
		public void Select_Binary_Subtract()
		{
			var query =
					from a in MyClass1Query.Query()
					select new { x = a.Prop2 - a.Prop2 };

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop2] - [a].[Prop2] AS [x]
FROM [dbo].[MyClass1] AS [a]",
					SqlLog);
		}

		[TestMethod]
		public void Select_Binary_Multiply()
		{
			var query =
					from a in MyClass1Query.Query()
					select new { x = a.Prop2 * a.Prop2 };

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop2] * [a].[Prop2] AS [x]
FROM [dbo].[MyClass1] AS [a]",
					SqlLog);
		}

		[TestMethod]
		public void Select_Binary_Divide()
		{
			var query =
					from a in MyClass1Query.Query()
					select new { x = a.Prop2 / a.Prop2 };

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop2] / [a].[Prop2] AS [x]
FROM [dbo].[MyClass1] AS [a]",
					SqlLog);
		}

		[TestMethod]
		public void Select_Binary_Modulo()
		{
			var query =
					from a in MyClass1Query.Query()
					select new { x = a.Prop2 % a.Prop2 };

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop2] % [a].[Prop2] AS [x]
FROM [dbo].[MyClass1] AS [a]",
					SqlLog);
		}

		[TestMethod]
		public void Select_Binary_And()
		{
			var query =
					from a in MyClass1Query.Query()
					select new { x = a.Prop2 & a.Prop2 };

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop2] & [a].[Prop2] AS [x]
FROM [dbo].[MyClass1] AS [a]",
					SqlLog);
		}

		[TestMethod]
		public void Select_Binary_Or()
		{
			var query =
					from a in MyClass1Query.Query()
					select new { x = a.Prop2 | a.Prop2 };

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop2] | [a].[Prop2] AS [x]
FROM [dbo].[MyClass1] AS [a]",
					SqlLog);
		}

		[TestMethod]
		public void Select_Binary_ExclusiveOr()
		{
			var query =
					from a in MyClass1Query.Query()
					select new { x = a.Prop2 ^ a.Prop2 };

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop2] ^ [a].[Prop2] AS [x]
FROM [dbo].[MyClass1] AS [a]",
					SqlLog);
		}

		[TestMethod]
		public void Select_Unary_OnesComplement()
		{
			var query =
					from a in MyClass1Query.Query()
					select new { x = ~a.Prop2 };

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT ~ [a].[Prop2] AS [x]
FROM [dbo].[MyClass1] AS [a]",
					SqlLog);
		}

		[TestMethod]
		public void Where_Equal()
		{
			var query =
					from a in MyClass1Query.Query()
					where a.Prop1 == "What the"
					select a;

			var results = query.ToList();

			Assert.AreEqual(1, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE [a].[Prop1] = N'What the'",
					SqlLog);
		}

		[TestMethod]
		public void Where_NotEqual()
		{
			var query =
					from a in MyClass1Query.Query()
					where a.Prop1 != "What the"
					select a;

			var results = query.ToList();

			Assert.AreEqual(1, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE [a].[Prop1] <> N'What the'",
					SqlLog);
		}

		[TestMethod]
		public void Where_GreaterThan()
		{
			var query =
					from a in MyClass1Query.Query()
					where a.Prop2 > 76
					select a;

			var results = query.ToList();

			Assert.AreEqual(1, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE [a].[Prop2] > 76",
					SqlLog);
		}

		[TestMethod]
		public void Where_GreaterThanEqual()
		{
			var query =
					from a in MyClass1Query.Query()
					where a.Prop2 >= 77
					select a;

			var results = query.ToList();

			Assert.AreEqual(1, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE [a].[Prop2] >= 77",
					SqlLog);
		}

		[TestMethod]
		public void Where_LessThan()
		{
			var query =
					from a in MyClass1Query.Query()
					where a.Prop2 < 10
					select a;

			var results = query.ToList();

			Assert.AreEqual(1, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE [a].[Prop2] < 10",
					SqlLog);
		}

		[TestMethod]
		public void Where_LessThanEqual()
		{
			var query =
					from a in MyClass1Query.Query()
					where a.Prop2 <= 9
					select a;

			var results = query.ToList();

			Assert.AreEqual(1, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE [a].[Prop2] <= 9",
					SqlLog);
		}

		[TestMethod]
		public void Where_AndAlso()
		{
			var query =
					from a in MyClass1Query.Query()
					where a.Prop1 == "What the" && a.Prop2 == 9
					select a;

			var results = query.ToList();

			Assert.AreEqual(1, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE ([a].[Prop1] = N'What the') AND ([a].[Prop2] = 9)",
					SqlLog);
		}

		[TestMethod]
		public void Where_OrElse()
		{
			var query =
					from a in MyClass1Query.Query()
					where a.Prop1 == "What the" || a.Prop2 == 77
					select a;

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE ([a].[Prop1] = N'What the') OR ([a].[Prop2] = 77)",
					SqlLog);
		}

		[TestMethod]
		public void Where_true()
		{
			var query =
					from a in MyClass1Query.Query()
					where true
					select a;

			query.ToList();

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE 1 = 1",
					SqlLog);
		}

		[TestMethod]
		public void Where_true2()
		{
			var query =
					from z in (from a in MyClass1Query.Query()
										 select new { x = a.Prop1, y = a.Prop2 < 9000 }).Take(10)
					where z.x != null && z.y
					select z;

			query.ToList();

			Assert.AreEqual(
					@"SELECT [z].[x] AS [x], [z].[y] AS [y]
FROM (
    SELECT TOP (10) [a].[Prop1] AS [x], CAST((CASE WHEN [a].[Prop2] < 9000 THEN 1 ELSE 0 END) AS BIT) AS [y]
    FROM [dbo].[MyClass1] AS [a]
) AS [z]
WHERE ([z].[x] IS NOT NULL) AND ([z].[y] = 1)",
					SqlLog);
		}

		[TestMethod]
		public void Where_false()
		{
			var query =
					from a in MyClass1Query.Query()
					where false
					select a;

			query.ToList();

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE 0 = 1",
					SqlLog);
		}

		[TestMethod]
		public void Where_Where()
		{
			var query =
					from a in MyClass1Query.Query()
					where a.Prop1 == "What the"
					where a.Prop2 == 9
					select a;

			var results = query.ToList();

			Assert.AreEqual(1, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE ([a].[Prop1] = N'What the') AND ([a].[Prop2] = 9)",
					SqlLog);
		}

		[TestMethod]
		public void Where_PartialClientEval_LeftSide()
		{
			var query =
					from a in MyClass1Query.Query()
					where a.Unmapped == 0 && a.Prop1 == "What the"
					select a;

			var results = query.ToList();

			Assert.AreEqual(1, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE [a].[Prop1] = N'What the'",
					SqlLog);
		}

		[TestMethod]
		public void Where_PartialClientEval_RightSide()
		{
			var query =
					from a in MyClass1Query.Query()
					where a.Prop1 == "What the" && a.Unmapped == 0
					select a;

			var results = query.ToList();

			Assert.AreEqual(1, results.Count);

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE [a].[Prop1] = N'What the'",
					SqlLog);
		}

		[TestMethod]
		public void Where_PartialClientEval_Multipart()
		{
			var query =
					from a in MyClass1Query.Query()
					where (a.Unmapped == 0 && a.Prop1 == "What the") && (a.Prop2 == 9 && a.Unmapped > -1)
					select a;

			var results = query.ToList();

			Assert.AreEqual(
					@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE ([a].[Prop1] = N'What the') AND ([a].[Prop2] = 9)",
					SqlLog);
		}

		[TestMethod]
		public void Where_with_index()
		{
			var query
					= MyClass1Query.Query()
							.OrderBy(m => m.Prop2)
							.Where((x, i) => i + x.Prop2 < 99);

			var results = query.ToList();

			Assert.AreEqual(
					@"SELECT [x].[Prop1] AS [Prop1], [x].[Prop2] AS [Prop2]
FROM (
    SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2], CAST(ROW_NUMBER() OVER(ORDER BY [m].[Prop2] ASC) - 1 AS int) AS [$rownumber]
    FROM [dbo].[MyClass1] AS [m]
) AS [x]
WHERE ([x].[$rownumber] + [x].[Prop2]) < 99",
					SqlLog);
		}

		[TestMethod]
		public void Where_with_index_no_ordering()
		{
			var query
					= MyClass1Query.Query()
							.Where((x, i) => i + x.Prop2 < 99);

			var results = query.ToList();

			Assert.AreEqual(
					@"SELECT [x].[Prop1] AS [Prop1], [x].[Prop2] AS [Prop2]
FROM (
    SELECT [x_0].[Prop1] AS [Prop1], [x_0].[Prop2] AS [Prop2], CAST(ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) - 1 AS int) AS [$rownumber]
    FROM [dbo].[MyClass1] AS [x_0]
) AS [x]
WHERE ([x].[$rownumber] + [x].[Prop2]) < 99",
					SqlLog);
		}

		[TestMethod]
		public void Where_after_Select_with_index_causes_pushdown()
		{
			var query
					= MyClass1Query.Query()
							.OrderBy(m => m.Prop2)
							.Select((x, i) => new { x, i })
							.Where(x => x.i + x.x.Prop2 < 99);

			var results = query.ToList();

			Assert.AreEqual(
					@"SELECT [x].[x.Prop1] AS [x.Prop1], [x].[x.Prop2] AS [x.Prop2], [x].[i] AS [i]
FROM (
    SELECT [m].[Prop1] AS [x.Prop1], [m].[Prop2] AS [x.Prop2], CAST(ROW_NUMBER() OVER(ORDER BY [m].[Prop2] ASC) - 1 AS int) AS [i]
    FROM [dbo].[MyClass1] AS [m]
) AS [x]
WHERE ([x].[i] + [x].[x.Prop2]) < 99",
					SqlLog);
		}

		[TestMethod]
		public void Count_simple()
		{
			var result = MyClass1Query.Query().Count();

			Assert.AreEqual(2, result);

			Assert.AreEqual(
					@"SELECT COUNT(*)
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Count_with_predicate()
		{
			var result = MyClass1Query.Query().Count(m => m.Prop1 == "What the");

			Assert.AreEqual(1, result);

			Assert.AreEqual(
					@"SELECT COUNT(*)
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop1] = N'What the'",
					SqlLog);
		}

		[TestMethod]
		public void LongCount_simple()
		{
			var result = MyClass1Query.Query().LongCount();

			Assert.AreEqual(2, result);

			Assert.AreEqual(
					@"SELECT COUNT_BIG(*)
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void LongCount_with_predicate()
		{
			var result = MyClass1Query.Query().LongCount(m => m.Prop1 == "What the");

			Assert.AreEqual(1, result);

			Assert.AreEqual(
					@"SELECT COUNT_BIG(*)
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop1] = N'What the'",
					SqlLog);
		}

		[TestMethod]
		public void Average_simple()
		{
			var result = MyClass1Query.Query().Select(m => m.Prop2).Average();

			Assert.AreEqual(43, result);

			Assert.AreEqual(
					@"SELECT AVG(CAST([m].[Prop2] AS float))
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Average_with_selector()
		{
			var result = MyClass1Query.Query().Average(m => m.Prop2);

			Assert.AreEqual(43, result);

			Assert.AreEqual(
					@"SELECT AVG(CAST([m].[Prop2] AS float))
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Max_simple()
		{
			var result = MyClass1Query.Query().Select(m => m.Prop2).Max();

			Assert.AreEqual(77, result);

			Assert.AreEqual(
					@"SELECT MAX([m].[Prop2])
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Max_with_selector()
		{
			var result = MyClass1Query.Query().Max(m => m.Prop2);

			Assert.AreEqual(77, result);

			Assert.AreEqual(
					@"SELECT MAX([m].[Prop2])
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Min_simple()
		{
			var result = MyClass1Query.Query().Select(m => m.Prop2).Min();

			Assert.AreEqual(9, result);

			Assert.AreEqual(
					@"SELECT MIN([m].[Prop2])
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Min_with_selector()
		{
			var result = MyClass1Query.Query().Min(m => m.Prop2);

			Assert.AreEqual(9, result);

			Assert.AreEqual(
					@"SELECT MIN([m].[Prop2])
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Sum_simple()
		{
			var result = MyClass1Query.Query().Select(m => m.Prop2).Sum();

			Assert.AreEqual(86, result);

			Assert.AreEqual(
					@"SELECT SUM([m].[Prop2])
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Sum_with_selector()
		{
			var result = MyClass1Query.Query().Sum(m => m.Prop2);

			Assert.AreEqual(86, result);

			Assert.AreEqual(
					@"SELECT SUM([m].[Prop2])
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Take_with_constant_count()
		{
			var query = MyClass1Query.Query().Take(1);

			var results = query.ToList();

			Assert.AreEqual(1, results.Count);

			Assert.AreEqual(
					@"SELECT TOP (1) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Skip_with_constant_count()
		{
			var query = MyClass1Query.Query().Skip(1);

			var results = query.ToList();

			Assert.AreEqual(1, results.Count);

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
OFFSET 1 ROWS",
					SqlLog);
		}

		[TestMethod]
		public void Any_without_predicate()
		{
			var result = MyClass1Query.Query().Where(m => m.Prop2 > 88).Any();

			Assert.IsFalse(result);

			Assert.AreEqual(
					@"SELECT CAST((CASE WHEN EXISTS (
    SELECT 1
    FROM [dbo].[MyClass1] AS [m]
    WHERE [m].[Prop2] > 88
) THEN 1 ELSE 0 END) AS BIT)",
					SqlLog);
		}

		[TestMethod]
		public void Any_with_predicate()
		{
			var result = MyClass1Query.Query().Any(m => m.Prop2 > 88);

			Assert.IsFalse(result);

			Assert.AreEqual(
					@"SELECT CAST((CASE WHEN EXISTS (
    SELECT 1
    FROM [dbo].[MyClass1] AS [m]
    WHERE [m].[Prop2] > 88
) THEN 1 ELSE 0 END) AS BIT)",
					SqlLog);
		}

		[TestMethod]
		public void All_simple()
		{
			var result = MyClass1Query.Query().All(m => m.Prop2 == 77);

			Assert.IsFalse(result);

			Assert.AreEqual(
					@"SELECT CAST((CASE WHEN NOT EXISTS (
    SELECT 1
    FROM [dbo].[MyClass1] AS [m]
    WHERE [m].[Prop2] <> 77
) THEN 1 ELSE 0 END) AS BIT)",
					SqlLog);
		}

		[TestMethod]
		public void Contains_subquery_simple()
		{
			var result = MyClass1Query.Query().Select(m => m.Prop2).Contains(77);

			Assert.IsTrue(result);

			Assert.AreEqual(
					@"SELECT CAST((CASE WHEN 77 IN (
    SELECT [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
) THEN 1 ELSE 0 END) AS BIT)",
					SqlLog);
		}

		[TestMethod]
		public void Contains_expression_in_array_of_translatable_expressions()
		{
			var query = MyClass1Query.Query().Select(m => new[] { m.Prop2 }.Contains(77));

			query.ToList();

			Assert.AreEqual(
					@"SELECT CAST((CASE WHEN 77 IN ([m].[Prop2]) THEN 1 ELSE 0 END) AS BIT)
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Contains_expression_in_array_literal()
		{
			var query = MyClass1Query.Query().Select(m => new[] { 77 }.Contains(m.Prop2));

			query.ToList();

			Assert.AreEqual(
					@"SELECT CAST((CASE WHEN [m].[Prop2] IN (77) THEN 1 ELSE 0 END) AS BIT)
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Contains_expression_in_array_closured()
		{
			var array = new[] { 77 };

			var query = MyClass1Query.Query().Select(m => array.Contains(m.Prop2));

			query.ToList();

			Assert.AreEqual(
					@"SELECT CAST((CASE WHEN [m].[Prop2] IN (@p0_0) THEN 1 ELSE 0 END) AS BIT)
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Contains_expression_in_list_literal()
		{
			var query = MyClass1Query.Query().Select(m => new List<int> { 77 }.Contains(m.Prop2));

			query.ToList();

			Assert.AreEqual(
					@"SELECT CAST((CASE WHEN [m].[Prop2] IN (77) THEN 1 ELSE 0 END) AS BIT)
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Contains_expression_in_list_closured()
		{
			var list = new List<int> { 77 };

			var query = MyClass1Query.Query().Select(m => list.Contains(m.Prop2));

			query.ToList();

			Assert.AreEqual(
					@"SELECT CAST((CASE WHEN [m].[Prop2] IN (@p0_0) THEN 1 ELSE 0 END) AS BIT)
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Distinct_simple()
		{
			var query = MyClass1Query.Query().Select(m => 1).Distinct();

			var results = query.ToList();

			Assert.AreEqual(1, results.Count);

			Assert.AreEqual(
					@"SELECT DISTINCT 1
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void OrderBy_simple()
		{
			var query = MyClass1Query.Query().OrderBy(m => m.Prop1);

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
ORDER BY [m].[Prop1] ASC",
					SqlLog);
		}

		[TestMethod]
		public void OrderBy_ThenByDescending()
		{
			var query = MyClass1Query.Query().OrderBy(m => m.Prop1).ThenByDescending(m => m.Prop2);

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
ORDER BY [m].[Prop1] ASC, [m].[Prop2] DESC",
					SqlLog);
		}

		[TestMethod]
		public void OrderBy_ThenBy_with_untranslatable_selector()
		{
			var query
					= MyClass1Query.Query()
							.OrderBy(m => m.Prop1)
							.ThenBy(m => m.Unmapped);

			query.ToList();

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Reverse_with_previous_ordering()
		{
			var query
					= MyClass1Query.Query()
							.OrderBy(m => m.Prop1)
							.ThenByDescending(m => m.Prop2)
							.Reverse();

			query.ToList();

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
ORDER BY [m].[Prop1] DESC, [m].[Prop2] ASC",
					SqlLog);
		}

		[TestMethod]
		public void Reverse_without_previous_ordering()
		{
			var query
					= MyClass1Query.Query()
							.Reverse();

			query.ToList();

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) DESC",
					SqlLog);
		}

		[TestMethod]
		public void Reverse_without_ordering_after_Skip_without_ordering()
		{
			var query
					= MyClass1Query.Query()
							.Skip(1)
							.Reverse();

			query.ToList();

			Assert.AreEqual(
					@"SELECT [t].[Prop1] AS [Prop1], [t].[Prop2] AS [Prop2]
FROM (
    SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
    ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
    OFFSET 1 ROWS
) AS [t]
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) DESC",
					SqlLog);
		}

		[TestMethod]
		public void Range_variable_1()
		{
			var query = from m in MyClass1Query.Query()
									let derp = int.MaxValue
									select m;

			var expression = impatient.ExpressionVisitorProvider.OptimizingExpressionVisitors
					.Aggregate(query.Expression, (e, v) => v.Visit(e));

			expression = new QueryComposingExpressionVisitor(impatient.ExpressionVisitorProvider).Visit(expression);

			Assert.IsInstanceOfType(expression, typeof(EnumerableRelationalQueryExpression));

			Assert.IsInstanceOfType(((EnumerableRelationalQueryExpression)expression).SelectExpression.Projection, typeof(ServerProjectionExpression));
		}

		[TestMethod]
		public void Subquery_in_selector_scalar()
		{
			var query = from m in MyClass1Query.Query()
									select MyClass2Query.Query().Count();

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);
			Assert.IsTrue(results.All(r => r == 2));

			Assert.AreEqual(
					@"SELECT (
    SELECT COUNT(*)
    FROM [dbo].[MyClass2] AS [m]
)
FROM [dbo].[MyClass1] AS [m_0]",
					SqlLog);
		}

		[TestMethod]
		public void OfType_passthrough()
		{
			var query = from m in MyClass1Query.Query().OfType<MyClass1>()
									select m;

			var expression = impatient.ExpressionVisitorProvider.OptimizingExpressionVisitors
					.Aggregate(query.Expression, (e, v) => v.Visit(e));

			expression = new QueryComposingExpressionVisitor(impatient.ExpressionVisitorProvider).Visit(expression);

			Assert.IsInstanceOfType(expression, typeof(EnumerableRelationalQueryExpression));

			Assert.IsInstanceOfType(((EnumerableRelationalQueryExpression)expression).SelectExpression.Projection, typeof(ServerProjectionExpression));
		}

		[TestMethod]
		public void GroupBy_Key_Element_Result()
		{
			var query
					= MyClass1Query.Query().GroupBy(
							m => m.Prop1,
							m => m,
							(k, ms) => new
							{
								Key = k,
								Max = ms.Max(m => m.Prop2),
								Min = ms.Select(m => m.Prop2).Distinct().Min(),
								Count = ms.Count(m => m.Prop2 > 7),
							});

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [Key], MAX([m].[Prop2]) AS [Max], MIN(DISTINCT [m].[Prop2]) AS [Min], COUNT((CASE WHEN [m].[Prop2] > 7 THEN 1 ELSE NULL END)) AS [Count]
FROM [dbo].[MyClass1] AS [m]
GROUP BY [m].[Prop1]",
					SqlLog);
		}

		[TestMethod]
		public void GroupBy_Key_Element_then_Select_continuation()
		{
			var query = from m in MyClass1Query.Query()
									group m by m.Prop1 into ms
									let max = ms.Max(m => m.Prop2)
									let min = ms.Select(m => m.Prop2).Distinct().Min()
									let count = ms.Count(m => m.Prop2 > 7)
									select new { ms.Key, max, min, count };

			var results = query.ToList();

			Assert.AreEqual(2, results.Count);

			Assert.AreEqual(
					@"SELECT [ms].[Prop1] AS [Key], MAX([ms].[Prop2]) AS [max], MIN(DISTINCT [ms].[Prop2]) AS [min], COUNT((CASE WHEN [ms].[Prop2] > 7 THEN 1 ELSE NULL END)) AS [count]
FROM [dbo].[MyClass1] AS [ms]
GROUP BY [ms].[Prop1]",
					SqlLog);
		}

		[TestMethod]
		public void GroupBy_as_grouping_top_level()
		{
			var query = from m in MyClass1Query.Query()
									group m by m.Prop1;

			query.ToList();

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [Key], (
    SELECT [m_0].[Prop1] AS [Prop1], [m_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
    WHERE [m].[Prop1] = [m_0].[Prop1]
    FOR JSON PATH, INCLUDE_NULL_VALUES
) AS [Elements]
FROM [dbo].[MyClass1] AS [m]
GROUP BY [m].[Prop1]",
					SqlLog);
		}

		[TestMethod]
		public void GroupBy_as_grouping_subquery()
		{
			var query = from m in MyClass1Query.Query()
									from g in (from s in MyClass1Query.Query()
														 group s by s.Prop1)
									select new { m, g };

			query.ToList().Select(x => x.g.ToArray()).ToArray();

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [m.Prop1], [m].[Prop2] AS [m.Prop2], [g].[Key] AS [g.Key], (
    SELECT [m_0].[Prop1] AS [Prop1], [m_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
    WHERE [g].[Key] = [m_0].[Prop1]
    FOR JSON PATH, INCLUDE_NULL_VALUES
) AS [g.Elements]
FROM [dbo].[MyClass1] AS [m]
CROSS JOIN (
    SELECT [m_1].[Prop1] AS [Key]
    FROM [dbo].[MyClass1] AS [m_1]
    GROUP BY [m_1].[Prop1]
) AS [g]",
					SqlLog);
		}

		[TestMethod]
		public void GroupJoin_SelectMany_as_InnerJoin()
		{
			var set1 = MyClass1Query.Query();
			var set2 = MyClass2Query.Query();

			var query = from s1 in set1
									join s2 in set2 on s1.Prop1 equals s2.Prop1 into g2
									from s2 in g2
									select new { s1, s2 };

			query.ToList();

			Assert.AreEqual(
					@"SELECT [s1].[Prop1] AS [s1.Prop1], [s1].[Prop2] AS [s1.Prop2], [s2].[Prop1] AS [s2.Prop1], [s2].[Prop2] AS [s2.Prop2]
FROM [dbo].[MyClass1] AS [s1]
INNER JOIN [dbo].[MyClass2] AS [s2] ON [s1].[Prop1] = [s2].[Prop1]",
					SqlLog);
		}

		[TestMethod]
		public void GroupJoin_SelectMany_as_LeftJoin()
		{
			var set1 = MyClass1Query.Query();
			var set2 = MyClass2Query.Query();

			var query = from s1 in set1
									join s2 in set2 on s1.Prop1 equals s2.Prop1 into g2
									from s2 in g2.DefaultIfEmpty()
									select new { s1, s2 };

			query.ToList();

			Assert.AreEqual(
					@"SELECT [s1].[Prop1] AS [s1.Prop1], [s1].[Prop2] AS [s1.Prop2], [s2].[$empty] AS [s2.$empty], [s2].[Prop1] AS [s2.Prop1], [s2].[Prop2] AS [s2.Prop2]
FROM [dbo].[MyClass1] AS [s1]
LEFT JOIN (
    SELECT 0 AS [$empty], [s2_0].[Prop1] AS [Prop1], [s2_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass2] AS [s2_0]
) AS [s2] ON [s1].[Prop1] = [s2].[Prop1]",
					SqlLog);
		}

		[TestMethod]
		public void GroupJoin_SelectMany_as_LeftJoin_when_inner_is_null()
		{
			var set1 = MyClass1Query.Query();
			var set2 = MyClass2Query.Query();

			var query = from s1 in set1
									join s2 in set2 on s1.Prop2 equals s2.Prop2 + 1 into g2
									from s2 in g2.DefaultIfEmpty()
									select new { s1, s2 };

			query.ToList();

			Assert.AreEqual(
					@"SELECT [s1].[Prop1] AS [s1.Prop1], [s1].[Prop2] AS [s1.Prop2], [s2].[$empty] AS [s2.$empty], [s2].[Prop1] AS [s2.Prop1], [s2].[Prop2] AS [s2.Prop2]
FROM [dbo].[MyClass1] AS [s1]
LEFT JOIN (
    SELECT 0 AS [$empty], [s2_0].[Prop1] AS [Prop1], [s2_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass2] AS [s2_0]
) AS [s2] ON [s1].[Prop2] = ([s2].[Prop2] + 1)",
					SqlLog);
		}

		[TestMethod]
		public void GroupJoin_SelectMany_as_OuterApply()
		{
			var set1 = MyClass1Query.Query();
			var set2 = MyClass2Query.Query();

			var query = from s1 in set1
									join s2 in set2 on s1.Prop1 equals s2.Prop1 into g2
									from s2 in g2.Take(1).DefaultIfEmpty()
									select new { s1, s2 };

			query.ToList();

			Assert.AreEqual(
					@"SELECT [s1].[Prop1] AS [s1.Prop1], [s1].[Prop2] AS [s1.Prop2], [s2].[$empty] AS [s2.$empty], [s2].[Prop1] AS [s2.Prop1], [s2].[Prop2] AS [s2.Prop2]
FROM [dbo].[MyClass1] AS [s1]
OUTER APPLY (
    SELECT TOP (1) 0 AS [$empty], [s2_0].[Prop1] AS [Prop1], [s2_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass2] AS [s2_0]
    WHERE [s1].[Prop1] = [s2_0].[Prop1]
) AS [s2]",
					SqlLog);
		}

		[TestMethod]
		public void Cast_same_type_method()
		{
			var query = from m in MyClass1Query.Query().Cast<MyClass1>()
									select new { m };

			query.ToList();

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [m.Prop1], [m].[Prop2] AS [m.Prop2]
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Cast_same_type_expression()
		{
			var query = from MyClass1 m in MyClass1Query.Query()
									select new { m };

			query.ToList();

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [m.Prop1], [m].[Prop2] AS [m.Prop2]
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Concat_simple()
		{
			var set1 = MyClass1Query.Query().Select(m => new { m.Prop1, m.Prop2 });
			var set2 = MyClass2Query.Query().Select(m => new { m.Prop1, m.Prop2 });

			var query = set1.Concat(set2);

			var result = query.ToList();

			Assert.AreEqual(4, result.Count);

			Assert.AreEqual(
					@"SELECT [set].[Prop1] AS [Prop1], [set].[Prop2] AS [Prop2]
FROM (
    SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
    UNION ALL
    SELECT [m_0].[Prop1] AS [Prop1], [m_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass2] AS [m_0]
) AS [set]",
					SqlLog);
		}

		[TestMethod]
		public void Except_simple()
		{
			var set1 = MyClass1Query.Query().Select(m => new { m.Prop1, m.Prop2 });
			var set2 = MyClass2Query.Query().Where(m => m.Prop2 == 77).Select(m => new { m.Prop1, m.Prop2 });

			var query = set1.Except(set2);

			var result = query.ToList();

			Assert.AreEqual(1, result.Count);

			Assert.AreEqual(
					@"SELECT [set].[Prop1] AS [Prop1], [set].[Prop2] AS [Prop2]
FROM (
    SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
    EXCEPT
    SELECT [m_0].[Prop1] AS [Prop1], [m_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass2] AS [m_0]
    WHERE [m_0].[Prop2] = 77
) AS [set]",
					SqlLog);
		}

		[TestMethod]
		public void Intersect_simple()
		{
			var set1 = MyClass1Query.Query().Select(m => new { m.Prop1, m.Prop2 });
			var set2 = MyClass2Query.Query().Where(m => m.Prop2 == 77).Select(m => new { m.Prop1, m.Prop2 });

			var query = set1.Intersect(set2);

			var result = query.ToList();

			Assert.AreEqual(1, result.Count);

			Assert.AreEqual(
					@"SELECT [set].[Prop1] AS [Prop1], [set].[Prop2] AS [Prop2]
FROM (
    SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
    INTERSECT
    SELECT [m_0].[Prop1] AS [Prop1], [m_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass2] AS [m_0]
    WHERE [m_0].[Prop2] = 77
) AS [set]",
					SqlLog);
		}

		[TestMethod]
		public void Union_simple()
		{
			var set1 = MyClass1Query.Query().Select(m => new { m.Prop1, m.Prop2 });
			var set2 = MyClass2Query.Query().Select(m => new { m.Prop1, m.Prop2 });

			var query = set1.Union(set2);

			var result = query.ToList();

			Assert.AreEqual(2, result.Count);

			Assert.AreEqual(
					@"SELECT [set].[Prop1] AS [Prop1], [set].[Prop2] AS [Prop2]
FROM (
    SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
    UNION
    SELECT [m_0].[Prop1] AS [Prop1], [m_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass2] AS [m_0]
) AS [set]",
					SqlLog);
		}

		[TestMethod]
		public void DefaultIfEmpty_simple_when_some()
		{
			var query = MyClass1Query.Query().DefaultIfEmpty();

			var result = query.ToList();

			Assert.AreEqual(2, result.Count);

			Assert.AreEqual(
					@"SELECT [t].[$empty] AS [$empty], [t].[Prop1] AS [Prop1], [t].[Prop2] AS [Prop2]
FROM (
    SELECT NULL AS [$empty]
) AS [t_0]
LEFT JOIN (
    SELECT 0 AS [$empty], [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [t] ON 1 = 1",
					SqlLog);
		}

		[TestMethod]
		public void DefaultIfEmpty_simple_when_none()
		{
			var query = MyClass1Query.Query().Where(m => m.Prop2 > 77).DefaultIfEmpty();

			var result = query.ToList();

			Assert.AreEqual(1, result.Count);
			Assert.AreEqual(null, result[0]);

			Assert.AreEqual(
					@"SELECT [t].[$empty] AS [$empty], [t].[Prop1] AS [Prop1], [t].[Prop2] AS [Prop2]
FROM (
    SELECT NULL AS [$empty]
) AS [t_0]
LEFT JOIN (
    SELECT 0 AS [$empty], [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
    WHERE [m].[Prop2] > 77
) AS [t] ON 1 = 1",
					SqlLog);
		}

		[TestMethod]
		public void DefaultIfEmpty_simple_when_none_with_default_value()
		{
			var query = MyClass1Query.Query().Where(m => m.Prop2 > 77).DefaultIfEmpty(new MyClass1());

			Assert.IsInstanceOfType(query.Expression, typeof(MethodCallExpression));
		}

		[TestMethod]
		public void Single_simple_no_predicate()
		{
			MyClass1Query.Query().Where(m => m.Prop2 == 77).Single();

			Assert.AreEqual(
					@"SELECT TOP (2) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] = 77",
					SqlLog);
		}

		[TestMethod]
		public void Single_simple_predicate()
		{
			MyClass1Query.Query().Single(m => m.Prop2 == 77);

			Assert.AreEqual(
					@"SELECT TOP (2) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] = 77",
					SqlLog);
		}

		[TestMethod]
		public void Single_simple_when_none()
		{
			Assert.ThrowsException<InvalidOperationException>(() => MyClass1Query.Query().Where(m => m.Prop2 > 77).Single());

			Assert.AreEqual(
					@"SELECT TOP (2) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] > 77",
					SqlLog);
		}

		[TestMethod]
		public void Single_simple_when_many()
		{
			Assert.ThrowsException<InvalidOperationException>(() => MyClass1Query.Query().Single());

			Assert.AreEqual(
					@"SELECT TOP (2) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void SingleOrDefault_simple_no_predicate()
		{
			MyClass1Query.Query().Where(m => m.Prop2 == 77).SingleOrDefault();

			Assert.AreEqual(
					@"SELECT TOP (2) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] = 77",
					SqlLog);
		}

		[TestMethod]
		public void SingleOrDefault_simple_predicate()
		{
			MyClass1Query.Query().SingleOrDefault(m => m.Prop2 == 77);

			Assert.AreEqual(
					@"SELECT TOP (2) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] = 77",
					SqlLog);
		}

		[TestMethod]
		public void SingleOrDefault_simple_when_none()
		{
			MyClass1Query.Query().SingleOrDefault(m => m.Prop2 > 77);

			Assert.AreEqual(
					@"SELECT TOP (2) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] > 77",
					SqlLog);
		}

		[TestMethod]
		public void SingleOrDefault_simple_when_many()
		{
			Assert.ThrowsException<InvalidOperationException>(() => MyClass1Query.Query().SingleOrDefault());

			Assert.AreEqual(
					@"SELECT TOP (2) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void First_simple_no_predicate()
		{
			MyClass1Query.Query().Where(m => m.Prop2 == 77).First();

			Assert.AreEqual(
					@"SELECT TOP (1) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] = 77",
					SqlLog);
		}

		[TestMethod]
		public void First_simple_predicate()
		{
			MyClass1Query.Query().First(m => m.Prop2 == 77);

			Assert.AreEqual(
					@"SELECT TOP (1) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] = 77",
					SqlLog);
		}

		[TestMethod]
		public void First_simple_when_none()
		{
			Assert.ThrowsException<InvalidOperationException>(() => MyClass1Query.Query().Where(m => m.Prop2 > 77).First());

			Assert.AreEqual(
					@"SELECT TOP (1) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] > 77",
					SqlLog);
		}

		[TestMethod]
		public void First_simple_when_many()
		{
			MyClass1Query.Query().First();

			Assert.AreEqual(
					@"SELECT TOP (1) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void FirstOrDefault_simple_no_predicate()
		{
			MyClass1Query.Query().Where(m => m.Prop2 == 77).FirstOrDefault();

			Assert.AreEqual(
					@"SELECT TOP (1) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] = 77",
					SqlLog);
		}

		[TestMethod]
		public void FirstOrDefault_simple_predicate()
		{
			MyClass1Query.Query().FirstOrDefault(m => m.Prop2 == 77);

			Assert.AreEqual(
					@"SELECT TOP (1) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] = 77",
					SqlLog);
		}

		[TestMethod]
		public void FirstOrDefault_simple_when_none()
		{
			MyClass1Query.Query().FirstOrDefault(m => m.Prop2 > 77);

			Assert.AreEqual(
					@"SELECT TOP (1) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] > 77",
					SqlLog);
		}

		[TestMethod]
		public void FirstOrDefault_simple_when_many()
		{
			MyClass1Query.Query().FirstOrDefault();

			Assert.AreEqual(
					@"SELECT TOP (1) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]",
					SqlLog);
		}

		[TestMethod]
		public void Last_simple_no_predicate()
		{
			MyClass1Query.Query().Where(m => m.Prop2 == 77).Last();

			Assert.AreEqual(
					@"SELECT TOP (1) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] = 77
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) DESC",
					SqlLog);
		}

		[TestMethod]
		public void Last_simple_predicate()
		{
			MyClass1Query.Query().Last(m => m.Prop2 == 77);

			Assert.AreEqual(
					@"SELECT TOP (1) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] = 77
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) DESC",
					SqlLog);
		}

		[TestMethod]
		public void Last_simple_when_none()
		{
			Assert.ThrowsException<InvalidOperationException>(() => MyClass1Query.Query().Where(m => m.Prop2 > 77).Last());

			Assert.AreEqual(
					@"SELECT TOP (1) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] > 77
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) DESC",
					SqlLog);
		}

		[TestMethod]
		public void Last_simple_when_many()
		{
			MyClass1Query.Query().Last();

			Assert.AreEqual(
					@"SELECT TOP (1) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) DESC",
					SqlLog);
		}

		[TestMethod]
		public void LastOrDefault_simple_no_predicate()
		{
			MyClass1Query.Query().Where(m => m.Prop2 == 77).LastOrDefault();

			Assert.AreEqual(
					@"SELECT TOP (1) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] = 77
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) DESC",
					SqlLog);
		}

		[TestMethod]
		public void LastOrDefault_simple_predicate()
		{
			MyClass1Query.Query().LastOrDefault(m => m.Prop2 == 77);

			Assert.AreEqual(
					@"SELECT TOP (1) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] = 77
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) DESC",
					SqlLog);
		}

		[TestMethod]
		public void LastOrDefault_simple_when_none()
		{
			MyClass1Query.Query().LastOrDefault(m => m.Prop2 > 77);

			Assert.AreEqual(
					@"SELECT TOP (1) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] > 77
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) DESC",
					SqlLog);
		}

		[TestMethod]
		public void LastOrDefault_simple_when_many()
		{
			MyClass1Query.Query().LastOrDefault();

			Assert.AreEqual(
					@"SELECT TOP (1) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) DESC",
					SqlLog);
		}

		[TestMethod]
		public void ElementAt_simple()
		{
			MyClass1Query.Query().Where(m => m.Prop2 == 77).ElementAt(0);

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] = 77
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY",
					SqlLog);
		}

		[TestMethod]
		public void ElementAt_simple_when_none()
		{
			Assert.ThrowsException<ArgumentOutOfRangeException>(() => MyClass1Query.Query().Where(m => m.Prop2 > 77).ElementAt(0));

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] > 77
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY",
					SqlLog);
		}

		[TestMethod]
		public void ElementAt_simple_when_many()
		{
			MyClass1Query.Query().ElementAt(0);

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY",
					SqlLog);
		}

		[TestMethod]
		public void ElementAtOrDefault_simple()
		{
			MyClass1Query.Query().Where(m => m.Prop2 == 77).ElementAtOrDefault(0);

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] = 77
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY",
					SqlLog);
		}

		[TestMethod]
		public void ElementAtOrDefault_simple_when_none()
		{
			MyClass1Query.Query().Where(m => m.Prop2 > 77).ElementAtOrDefault(0);

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] > 77
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY",
					SqlLog);
		}

		[TestMethod]
		public void ElementAtOrDefault_simple_when_many()
		{
			MyClass1Query.Query().ElementAtOrDefault(0);

			Assert.AreEqual(
					@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY",
					SqlLog);
		}

		[TestMethod]
		public void Nested_collection_1_level_simple()
		{
			var query = from m in MyClass1Query.Query()
									select new
									{
										m.Prop2,
										m2s = (from m2 in MyClass2Query.Query()

																	 select m2).ToArray()

												};

		var results = query.ToList();

		Assert.AreEqual(2, results.Count);
            Assert.AreEqual(4, results.SelectMany(r => r.m2s).Count());

            Assert.AreEqual(
                @"SELECT [m].[Prop2] AS [Prop2], (
    SELECT [m2].[Prop1] AS [Prop1], [m2].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass2] AS [m2]
    FOR JSON PATH, INCLUDE_NULL_VALUES
) AS [m2s]
FROM [dbo].[MyClass1] AS [m]",
                SqlLog);
        }

	[TestMethod]
	public void Nested_collection_2_level_simple()
	{
		var query = from m in MyClass1Query.Query()
								select new
								{
									m.Prop2,
									m2s = (from m2 in MyClass2Query.Query()
                                   select new
																					{
																						m2.Prop1,
																						m2.Prop2,
																						m1s = (from m1 in MyClass1Query.Query()
																									 select new
																									 {
																										 a = m1.Prop1,
																										 b = m1.Prop2,
																										 x = new
																										 {
																											 y = m1.Prop2 * m2.Prop2
																										 }
																									 }).ToList()
																					}).ToArray()

												};

	var results = query.ToList();

	Assert.AreEqual(2, results.Count);
            Assert.AreEqual(8, results.SelectMany(r => r.m2s).SelectMany(r => r.m1s).Count());

            Assert.IsTrue(Enumerable.SequenceEqual(
								first: results.SelectMany(r => r.m2s).SelectMany(r => r.m1s).Select(r => r.x.y),
                second: results.SelectMany(r => r.m2s).SelectMany(r => r.m1s, (m2, m1) => new { m2, m1
}).Select(x => x.m2.Prop2* x.m1.b)));

            Assert.AreEqual(
                @"SELECT [m].[Prop2] AS [Prop2], (
    SELECT [m2].[Prop1] AS [Prop1], [m2].[Prop2] AS [Prop2], (
        SELECT [m1].[Prop1] AS [a], [m1].[Prop2] AS [b], [m1].[Prop2] * [m2].[Prop2] AS [x.y]
        FROM [dbo].[MyClass1] AS [m1]
        FOR JSON PATH, INCLUDE_NULL_VALUES
    ) AS [m1s]
    FROM [dbo].[MyClass2] AS [m2]
    FOR JSON PATH, INCLUDE_NULL_VALUES
) AS [m2s]
FROM [dbo].[MyClass1] AS [m]",
                SqlLog);
        }

        [TestMethod]
public void GroupBy_aggregations_and_selections()
{
	var query = from m in MyClass1Query.Query()
							group m by m.Prop1 into ms
							let max = ms.Max(m => m.Prop2)
							let min = ms.Select(m => m.Prop2).Distinct().Min()
							let count = ms.Count(m => m.Prop2 > 7)
							from m in ms
							select new { m, ms.Key, max, min, count, sum = ms.Sum(x => x.Prop2) };

	query.ToList();

	Assert.AreEqual(
			@"SELECT [ms].[Prop1] AS [m.Prop1], [ms].[Prop2] AS [m.Prop2], [t].[ms.Key] AS [Key], [t].[max] AS [max], [t].[min] AS [min], [t].[count] AS [count], (
    SELECT SUM([ms_0].[Prop2])
    FROM [dbo].[MyClass1] AS [ms_0]
    WHERE [t].[ms.Key] = [ms_0].[Prop1]
) AS [sum]
FROM (
    SELECT [ms_1].[Prop1] AS [ms.Key], MAX([ms_1].[Prop2]) AS [max], MIN(DISTINCT [ms_1].[Prop2]) AS [min], COUNT((CASE WHEN [ms_1].[Prop2] > 7 THEN 1 ELSE NULL END)) AS [count]
    FROM [dbo].[MyClass1] AS [ms_1]
    GROUP BY [ms_1].[Prop1]
) AS [t]
INNER JOIN [dbo].[MyClass1] AS [ms] ON [t].[ms.Key] = [ms].[Prop1]",
			SqlLog);
}

[TestMethod]
public void GroupJoin_aggregations_and_selections()
{
	var query = from m1 in MyClass1Query.Query()
							join m2 in MyClass2Query.Query()
                            on m1.Prop1 equals m2.Prop1 into m2s
												from m2 in m2s
												select new
												{
													m1,
													m2,
													sum = m2s.Sum(x => x.Prop2),
												};

	query.ToList();

	Assert.AreEqual(
			@"SELECT [m1].[Prop1] AS [m1.Prop1], [m1].[Prop2] AS [m1.Prop2], [m2].[Prop1] AS [m2.Prop1], [m2].[Prop2] AS [m2.Prop2], (
    SELECT SUM([m2_0].[Prop2])
    FROM [dbo].[MyClass2] AS [m2_0]
    WHERE [m1].[Prop1] = [m2_0].[Prop1]
) AS [sum]
FROM [dbo].[MyClass1] AS [m1]
INNER JOIN [dbo].[MyClass2] AS [m2] ON [m1].[Prop1] = [m2].[Prop1]",
			SqlLog);
}

[TestMethod]
public void GroupJoin_aggregations_and_selections_2()
{
	var query = from sub in (from m1 in MyClass1Query.Query()
													 join m2 in MyClass2Query.Query()

																				 on m1.Prop1 equals m2.Prop1 into m2s
																		 from m2 in m2s
																		 select new
																		 {
																			 m1,
																			 m2,
																			 sum = m2s.Sum(x => x.Prop2),
																			 m2s,
																		 })
                        from m2 in sub.m2s
												select new
												{
													sub,
													m2,
												};

	query.ToList();

	Assert.AreEqual(
			@"SELECT [m1].[Prop1] AS [sub.m1.Prop1], [m1].[Prop2] AS [sub.m1.Prop2], [m2].[Prop1] AS [sub.m2.Prop1], [m2].[Prop2] AS [sub.m2.Prop2], (
    SELECT SUM([m2_0].[Prop2])
    FROM [dbo].[MyClass2] AS [m2_0]
    WHERE [m1].[Prop1] = [m2_0].[Prop1]
) AS [sub.sum], (
    SELECT [m2_0].[Prop1] AS [Prop1], [m2_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass2] AS [m2_0]
    WHERE [m1].[Prop1] = [m2_0].[Prop1]
    FOR JSON PATH, INCLUDE_NULL_VALUES
) AS [sub.m2s], [m2_1].[Prop1] AS [m2.Prop1], [m2_1].[Prop2] AS [m2.Prop2]
FROM [dbo].[MyClass1] AS [m1]
INNER JOIN [dbo].[MyClass2] AS [m2] ON [m1].[Prop1] = [m2].[Prop1]
INNER JOIN [dbo].[MyClass2] AS [m2_1] ON [m1].[Prop1] = [m2_1].[Prop1]",
			SqlLog);
}

[TestMethod]
public void GroupJoin_floated_up_from_subquery()
{
	var sqlLog = new StringBuilder();

	var impatient = new ImpatientQueryProvider(
			new TestImpatientConnectionFactory(@"Server=.\sqlexpress; Database=NORTHWND; Trusted_Connection=True"),
			new DefaultImpatientQueryCache(),
			new DefaultImpatientExpressionVisitorProvider())
	{
		DbCommandInterceptor = command =>
		{
			if (sqlLog.Length > 0)
			{
				sqlLog.AppendLine().AppendLine();
			}

			sqlLog.Append(command.CommandText);
		}
	};

			var customers = new Queryable<Northwind.Customer>(impatient);
			var orders = new Queryable<Northwind.Order>(impatient);
			var details = new Queryable<Northwind.OrderDetail>(impatient);

	var query = from c in customers.Query()
							join x in (from o in orders.Query()
												 join d in details.Query() on o.OrderID equals d.OrderID into dg
												 select new { o, dg }) on c.CustomerID equals x.o.CustomerID into xg
							select new
							{
								c,
								TotalOrders = xg.Count(),
								TotalDetails = xg.Sum(x => x.dg.Count()),
							};

	query.ToList();

	Assert.IsTrue(
			sqlLog.ToString().StartsWith(
					@"SELECT [c].[CustomerID] AS [CustomerID], [c].[CompanyName] AS [CompanyName], [c].[ContactName] AS [ContactName], [c].[ContactTitle] AS [ContactTitle], [c].[Address] AS [Address], [c].[City] AS [City], [c].[Region] AS [Region], [c].[PostalCode] AS [PostalCode], [c].[Country] AS [Country], [c].[Phone] AS [Phone], [c].[Fax] AS [Fax]
FROM [dbo].[Customers] AS [c]

SELECT COUNT(*)
FROM [dbo].[Orders] AS [o]
WHERE @p0 = [o].[CustomerID]

SELECT (
    SELECT COUNT(*)
    FROM [dbo].[Order Details] AS [d]
    WHERE [o].[OrderID] = [d].[OrderID]
)
FROM [dbo].[Orders] AS [o]
WHERE @p0 = [o].[CustomerID]"));
}

[TestMethod]
public void Distinct_causes_outer_pushdown()
{
	var query = from m1 in MyClass1Query.Query().Distinct()
							join m2 in MyClass2Query.Query()
                            on m1.Prop2 equals m2.Prop2
												select new { m1, m2 };

	query.ToList();

	Assert.AreEqual(
			@"SELECT [m1].[Prop1] AS [m1.Prop1], [m1].[Prop2] AS [m1.Prop2], [m2].[Prop1] AS [m2.Prop1], [m2].[Prop2] AS [m2.Prop2]
FROM (
    SELECT DISTINCT [m1_0].[Prop1] AS [Prop1], [m1_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m1_0]
) AS [m1]
INNER JOIN [dbo].[MyClass2] AS [m2] ON [m1].[Prop2] = [m2].[Prop2]",
			SqlLog);
}

[TestMethod]
public void Take_causes_outer_pushdown()
{
	var query = from m1 in MyClass1Query.Query().Take(1)
							join m2 in MyClass2Query.Query()
                            on m1.Prop2 equals m2.Prop2
												select new { m1, m2 };

	query.ToList();

	Assert.AreEqual(
			@"SELECT [m1].[Prop1] AS [m1.Prop1], [m1].[Prop2] AS [m1.Prop2], [m2].[Prop1] AS [m2.Prop1], [m2].[Prop2] AS [m2.Prop2]
FROM (
    SELECT TOP (1) [m1_0].[Prop1] AS [Prop1], [m1_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m1_0]
) AS [m1]
INNER JOIN [dbo].[MyClass2] AS [m2] ON [m1].[Prop2] = [m2].[Prop2]",
			SqlLog);
}

[TestMethod]
public void Skip_causes_outer_pushdown()
{
	var query = from m1 in MyClass1Query.Query().Skip(1)
							join m2 in MyClass2Query.Query()
                            on m1.Prop2 equals m2.Prop2
												select new { m1, m2 };

	query.ToList();

	Assert.AreEqual(
			@"SELECT [m1].[Prop1] AS [m1.Prop1], [m1].[Prop2] AS [m1.Prop2], [m2].[Prop1] AS [m2.Prop1], [m2].[Prop2] AS [m2.Prop2]
FROM (
    SELECT [m1_0].[Prop1] AS [Prop1], [m1_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m1_0]
    ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
    OFFSET 1 ROWS
) AS [m1]
INNER JOIN [dbo].[MyClass2] AS [m2] ON [m1].[Prop2] = [m2].[Prop2]",
			SqlLog);
}

[TestMethod]
public void GroupBy_causes_outer_pushdown()
{
	var query = from m1 in (from m1 in MyClass1Query.Query()
													group m1 by m1.Prop2 into m1g
													select new { m1g.Key, Sum = m1g.Sum(m1 => m1.Prop2) })
							join m2 in MyClass2Query.Query()
                            on m1.Key equals m2.Prop2
												select new { m1, m2 };

	query.ToList();

	Assert.AreEqual(
			@"SELECT [m1].[Key] AS [m1.Key], [m1].[Sum] AS [m1.Sum], [m2].[Prop1] AS [m2.Prop1], [m2].[Prop2] AS [m2.Prop2]
FROM (
    SELECT [m1g].[Prop2] AS [Key], SUM([m1g].[Prop2]) AS [Sum]
    FROM [dbo].[MyClass1] AS [m1g]
    GROUP BY [m1g].[Prop2]
) AS [m1]
INNER JOIN [dbo].[MyClass2] AS [m2] ON [m1].[Key] = [m2].[Prop2]",
			SqlLog);
}

[TestMethod]
public void Materialize_with_constructor()
{
	var table = new BaseTableExpression("dbo", "MyClass1", "m", typeof(MyClass1));

	var properties = new[]
	{
								typeof(MyClass1).GetRuntimeProperty(nameof(MyClass1.Prop1)),
								typeof(MyClass1).GetRuntimeProperty(nameof(MyClass1.Prop2))
						};

	var materializer
			= Expression.New(
					constructor: typeof(MyClass1).GetConstructor(new[] { typeof(string), typeof(int) }),
					arguments: from p in properties select new SqlColumnExpression(table, p.Name, p.PropertyType),
					members: properties);

	var queryExpression
			= new EnumerableRelationalQueryExpression(
					new SelectExpression(
							new ServerProjectionExpression(materializer),
							table));

	var query = from m in impatient.CreateQuery<MyClass1>(queryExpression)
							where m.Prop2 == 77
							select m;

	query.ToList();

	Assert.AreEqual(
			@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
WHERE [m].[Prop2] = 77",
			SqlLog);
}

[TestMethod]
public void Take_after_Take()
{
	var query = MyClass1Query.Query().Take(2).Take(1);

	query.ToList();

	Assert.AreEqual(
			@"SELECT TOP (1) [t].[Prop1] AS [Prop1], [t].[Prop2] AS [Prop2]
FROM (
    SELECT TOP (2) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Take_after_Skip()
{
	var query = MyClass1Query.Query().Skip(2).Take(1);

	query.ToList();

	Assert.AreEqual(
			@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
OFFSET 2 ROWS FETCH NEXT 1 ROWS ONLY",
			SqlLog);
}

[TestMethod]
public void Take_after_Distinct()
{
	var query = MyClass1Query.Query().Distinct().Take(1);

	query.ToList();

	Assert.AreEqual(
			@"SELECT TOP (1) [t].[Prop1] AS [Prop1], [t].[Prop2] AS [Prop2]
FROM (
    SELECT DISTINCT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Skip_after_Take()
{
	var query = MyClass1Query.Query().Take(2).Skip(1);

	query.ToList();

	Assert.AreEqual(
			@"SELECT [t].[Prop1] AS [Prop1], [t].[Prop2] AS [Prop2]
FROM (
    SELECT TOP (2) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [t]
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
OFFSET 1 ROWS",
			SqlLog);
}

[TestMethod]
public void Skip_after_Skip()
{
	var query = MyClass1Query.Query().Skip(2).Skip(1);

	query.ToList();

	Assert.AreEqual(
			@"SELECT [t].[Prop1] AS [Prop1], [t].[Prop2] AS [Prop2]
FROM (
    SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
    ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
    OFFSET 2 ROWS
) AS [t]
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
OFFSET 1 ROWS",
			SqlLog);
}

[TestMethod]
public void Skip_after_Distinct()
{
	var query = MyClass1Query.Query().Distinct().Skip(1);

	query.ToList();

	Assert.AreEqual(
			@"SELECT [t].[Prop1] AS [Prop1], [t].[Prop2] AS [Prop2]
FROM (
    SELECT DISTINCT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [t]
ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
OFFSET 1 ROWS",
			SqlLog);
}

[TestMethod]
public void OrderBy_boolean_expression()
{
	var query = from m in MyClass1Query.Query()
							orderby m.Prop2 == 77
							select m;

	query.ToList();

	Assert.AreEqual(
			@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]
ORDER BY (CASE WHEN [m].[Prop2] = 77 THEN 1 ELSE 0 END) ASC",
			SqlLog);
}

[TestMethod]
public void GroupBy_boolean_expression()
{
	var query = from m in MyClass1Query.Query()
							group m by m.Prop2 == 77 into mg
							select new { count = mg.Count() };

	query.ToList();

	Assert.AreEqual(
			@"SELECT COUNT(*) AS [count]
FROM [dbo].[MyClass1] AS [mg]
GROUP BY (CASE WHEN [mg].[Prop2] = 77 THEN 1 ELSE 0 END)",
			SqlLog);
}

[TestMethod]
public void Binary_Equal_complex_type_NewExpression()
{
	var query =
			from a in MyClass1Query.Query()
			join b in MyClass2Query.Query()
                    on a.Prop1 equals b.Prop1
								select new { a, b } == new { a, b };

	query.ToList();

	Assert.AreEqual(
			@"SELECT CAST((CASE WHEN (([a].[Prop1] = [a].[Prop1]) AND ([a].[Prop2] = [a].[Prop2])) AND (([b].[Prop1] = [b].[Prop1]) AND ([b].[Prop2] = [b].[Prop2])) THEN 1 ELSE 0 END) AS BIT)
FROM [dbo].[MyClass1] AS [a]
INNER JOIN [dbo].[MyClass2] AS [b] ON [a].[Prop1] = [b].[Prop1]",
			SqlLog);
}

[TestMethod]
public void Binary_NotEqual_complex_type_NewExpression()
{
	var query =
			from a in MyClass1Query.Query()
			join b in MyClass2Query.Query()
                    on a.Prop1 equals b.Prop1
								select new { a, b } != new { a, b };

	query.ToList();

	Assert.AreEqual(
			@"SELECT CAST((CASE WHEN (([a].[Prop1] <> [a].[Prop1]) OR ([a].[Prop2] <> [a].[Prop2])) OR (([b].[Prop1] <> [b].[Prop1]) OR ([b].[Prop2] <> [b].[Prop2])) THEN 1 ELSE 0 END) AS BIT)
FROM [dbo].[MyClass1] AS [a]
INNER JOIN [dbo].[MyClass2] AS [b] ON [a].[Prop1] = [b].[Prop1]",
			SqlLog);
}

[TestMethod]
public void Binary_Equal_complex_type_MemberInitExpression()
{
	var query =
			from a in MyClass1Query.Query()
			join b in MyClass2Query.Query()
                    on a.Prop1 equals b.Prop1
								select new MyKeyObject { Prop1 = a.Prop1, Prop2 = b.Prop2 } == new MyKeyObject { Prop1 = a.Prop1, Prop2 = b.Prop2 };

	query.ToList();

	Assert.AreEqual(
			@"SELECT CAST((CASE WHEN ([a].[Prop1] = [a].[Prop1]) AND ([b].[Prop2] = [b].[Prop2]) THEN 1 ELSE 0 END) AS BIT)
FROM [dbo].[MyClass1] AS [a]
INNER JOIN [dbo].[MyClass2] AS [b] ON [a].[Prop1] = [b].[Prop1]",
			SqlLog);
}

[TestMethod]
public void Binary_NotEqual_complex_type_MemberInitExpression()
{
	var query =
			from a in MyClass1Query.Query()
			join b in MyClass2Query.Query()
                    on a.Prop1 equals b.Prop1
								select new MyKeyObject { Prop1 = a.Prop1, Prop2 = b.Prop2 } != new MyKeyObject { Prop1 = a.Prop1, Prop2 = b.Prop2 };

	query.ToList();

	Assert.AreEqual(
			@"SELECT CAST((CASE WHEN ([a].[Prop1] <> [a].[Prop1]) OR ([b].[Prop2] <> [b].[Prop2]) THEN 1 ELSE 0 END) AS BIT)
FROM [dbo].[MyClass1] AS [a]
INNER JOIN [dbo].[MyClass2] AS [b] ON [a].[Prop1] = [b].[Prop1]",
			SqlLog);
}

[TestMethod]
public void Binary_Equal_null_constant()
{
	var query =
			from a in MyClass1Query.Query()
			where null == a.Prop1
			select a.Prop1 == null;

	query.ToList();

	Assert.AreEqual(
			@"SELECT CAST((CASE WHEN [a].[Prop1] IS NULL THEN 1 ELSE 0 END) AS BIT)
FROM [dbo].[MyClass1] AS [a]
WHERE [a].[Prop1] IS NULL",
			SqlLog);
}

[TestMethod]
public void Binary_NotEqual_null_constant()
{
	var query =
			from a in MyClass1Query.Query()
			where null != a.Prop1
			select a.Prop1 != null;

	query.ToList();

	Assert.AreEqual(
			@"SELECT CAST((CASE WHEN [a].[Prop1] IS NOT NULL THEN 1 ELSE 0 END) AS BIT)
FROM [dbo].[MyClass1] AS [a]
WHERE [a].[Prop1] IS NOT NULL",
			SqlLog);
}

[TestMethod]
public void Binary_NotEqual_nullable_left()
{
	var myClass1Table = new BaseTableExpression("dbo", "MyClass1", "m", typeof(MyClass1));

	var myClass1QueryExpression
			= new EnumerableRelationalQueryExpression(
					new SelectExpression(
							new ServerProjectionExpression(
									Expression.MemberInit(
											Expression.New(typeof(MyClass1)),
											from property in new[]
											{
																		typeof(MyClass1).GetRuntimeProperty(nameof(MyClass1.Prop1)),
																		typeof(MyClass1).GetRuntimeProperty(nameof(MyClass1.Prop2))
											}
											let column = new SqlColumnExpression(myClass1Table, property.Name, property.PropertyType, true)
											select Expression.Bind(property, column))),
							myClass1Table));

	var query =
			from a in impatient.CreateQuery<MyClass1>(myClass1QueryExpression)
			where a.Prop1 != "hello"
			select a;

	query.ToList();

	Assert.AreEqual(
			@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE ([a].[Prop1] IS NULL OR ([a].[Prop1] <> N'hello'))",
			SqlLog);
}

[TestMethod]
public void Binary_NotEqual_nullable_right()
{
	var myClass1Table = new BaseTableExpression("dbo", "MyClass1", "m", typeof(MyClass1));

	var myClass1QueryExpression
			= new EnumerableRelationalQueryExpression(
					new SelectExpression(
							new ServerProjectionExpression(
									Expression.MemberInit(
											Expression.New(typeof(MyClass1)),
											from property in new[]
											{
																		typeof(MyClass1).GetRuntimeProperty(nameof(MyClass1.Prop1)),
																		typeof(MyClass1).GetRuntimeProperty(nameof(MyClass1.Prop2))
											}
											let column = new SqlColumnExpression(myClass1Table, property.Name, property.PropertyType, true)
											select Expression.Bind(property, column))),
							myClass1Table));

	var query =
			from a in impatient.CreateQuery<MyClass1>(myClass1QueryExpression)
			where "hello" != a.Prop1
			select a;

	query.ToList();

	Assert.AreEqual(
			@"SELECT [a].[Prop1] AS [Prop1], [a].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [a]
WHERE ([a].[Prop1] IS NULL OR (N'hello' <> [a].[Prop1]))",
			SqlLog);
}

[TestMethod]
public void Binary_NotEqual_nullable_left_and_right()
{
	var myClass1Table = new BaseTableExpression("dbo", "MyClass1", "m", typeof(MyClass1));

	var myClass1QueryExpression
			= new EnumerableRelationalQueryExpression(
					new SelectExpression(
							new ServerProjectionExpression(
									Expression.MemberInit(
											Expression.New(typeof(MyClass1)),
											from property in new[]
											{
																		typeof(MyClass1).GetRuntimeProperty(nameof(MyClass1.Prop1)),
																		typeof(MyClass1).GetRuntimeProperty(nameof(MyClass1.Prop2))
											}
											let column = new SqlColumnExpression(myClass1Table, property.Name, property.PropertyType, true)
											select Expression.Bind(property, column))),
							myClass1Table));

	var myClass2Table = new BaseTableExpression("dbo", "MyClass2", "m", typeof(MyClass2));

	var myClass2QueryExpression
			= new EnumerableRelationalQueryExpression(
					new SelectExpression(
							new ServerProjectionExpression(
									Expression.MemberInit(
											Expression.New(typeof(MyClass2)),
											from property in new[]
											{
																		typeof(MyClass2).GetRuntimeProperty(nameof(MyClass2.Prop1)),
																		typeof(MyClass2).GetRuntimeProperty(nameof(MyClass2.Prop2))
											}
											let column = new SqlColumnExpression(myClass2Table, property.Name, property.PropertyType, true)
											select Expression.Bind(property, column))),
							myClass2Table));

	var query =
			from a in impatient.CreateQuery<MyClass1>(myClass1QueryExpression)
			from b in impatient.CreateQuery<MyClass2>(myClass2QueryExpression)
			where a.Prop1 != b.Prop1
			select new { a, b };

	query.ToList();

	Assert.AreEqual(
			@"SELECT [a].[Prop1] AS [a.Prop1], [a].[Prop2] AS [a.Prop2], [m].[Prop1] AS [b.Prop1], [m].[Prop2] AS [b.Prop2]
FROM [dbo].[MyClass1] AS [a]
CROSS JOIN [dbo].[MyClass2] AS [m]
WHERE (([a].[Prop1] IS NULL AND [m].[Prop1] IS NOT NULL) OR ([a].[Prop1] IS NOT NULL AND [m].[Prop1] IS NULL) OR ([a].[Prop1] <> [m].[Prop1]))",
			SqlLog);
}

[TestMethod]
public void Binary_Equal_nullable_left_and_right()
{
	var myClass1Table = new BaseTableExpression("dbo", "MyClass1", "m", typeof(MyClass1));

	var myClass1QueryExpression
			= new EnumerableRelationalQueryExpression(
					new SelectExpression(
							new ServerProjectionExpression(
									Expression.MemberInit(
											Expression.New(typeof(MyClass1)),
											from property in new[]
											{
																		typeof(MyClass1).GetRuntimeProperty(nameof(MyClass1.Prop1)),
																		typeof(MyClass1).GetRuntimeProperty(nameof(MyClass1.Prop2))
											}
											let column = new SqlColumnExpression(myClass1Table, property.Name, property.PropertyType, true)
											select Expression.Bind(property, column))),
							myClass1Table));

	var myClass2Table = new BaseTableExpression("dbo", "MyClass2", "m", typeof(MyClass2));

	var myClass2QueryExpression
			= new EnumerableRelationalQueryExpression(
					new SelectExpression(
							new ServerProjectionExpression(
									Expression.MemberInit(
											Expression.New(typeof(MyClass2)),
											from property in new[]
											{
																		typeof(MyClass2).GetRuntimeProperty(nameof(MyClass2.Prop1)),
																		typeof(MyClass2).GetRuntimeProperty(nameof(MyClass2.Prop2))
											}
											let column = new SqlColumnExpression(myClass2Table, property.Name, property.PropertyType, true)
											select Expression.Bind(property, column))),
							myClass2Table));

	var query =
			from a in impatient.CreateQuery<MyClass1>(myClass1QueryExpression)
			from b in impatient.CreateQuery<MyClass2>(myClass2QueryExpression)
			where a.Prop1 == b.Prop1
			select new { a, b };

	query.ToList();

	Assert.AreEqual(
			@"SELECT [a].[Prop1] AS [a.Prop1], [a].[Prop2] AS [a.Prop2], [m].[Prop1] AS [b.Prop1], [m].[Prop2] AS [b.Prop2]
FROM [dbo].[MyClass1] AS [a]
CROSS JOIN [dbo].[MyClass2] AS [m]
WHERE (([a].[Prop1] IS NULL AND [m].[Prop1] IS NULL) OR ([a].[Prop1] = [m].[Prop1]))",
			SqlLog);
}

[TestMethod]
public void Average_after_Distinct_causes_pushdown()
{
	var result = MyClass1Query.Query().Select(m => m.Prop2).Distinct().Average();

	Assert.AreEqual(
			@"SELECT AVG(CAST([t].[Prop2] AS float))
FROM (
    SELECT DISTINCT [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Average_after_Skip_causes_pushdown()
{
	var result = MyClass1Query.Query().Select(m => m.Prop2).Skip(1).Average();

	Assert.AreEqual(
			@"SELECT AVG(CAST([t].[Prop2] AS float))
FROM (
    SELECT [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
    ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
    OFFSET 1 ROWS
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Average_after_Take_causes_pushdown()
{
	var result = MyClass1Query.Query().Select(m => m.Prop2).Take(2).Average();

	Assert.AreEqual(
			@"SELECT AVG(CAST([t].[Prop2] AS float))
FROM (
    SELECT TOP (2) [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Average_after_GroupBy_causes_pushdown()
{
	var result = MyClass1Query.Query().GroupBy(m => m.Prop2).Select(g => g.Key).Average();

	Assert.AreEqual(
			@"SELECT AVG(CAST([t].[Prop2] AS float))
FROM (
    SELECT [g].[Prop2]
    FROM [dbo].[MyClass1] AS [g]
    GROUP BY [g].[Prop2]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Average_with_selector_after_Distinct_causes_pushdown()
{
	var result = MyClass1Query.Query().Distinct().Average(m => m.Prop2);

	Assert.AreEqual(
			@"SELECT AVG(CAST([m].[Prop2] AS float))
FROM (
    SELECT DISTINCT [m_0].[Prop1] AS [Prop1], [m_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
) AS [m]",
			SqlLog);
}

[TestMethod]
public void Average_with_selector_after_Skip_causes_pushdown()
{
	var result = MyClass1Query.Query().Skip(1).Average(m => m.Prop2);

	Assert.AreEqual(
			@"SELECT AVG(CAST([m].[Prop2] AS float))
FROM (
    SELECT [m_0].[Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
    ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
    OFFSET 1 ROWS
) AS [m]",
			SqlLog);
}

[TestMethod]
public void Average_with_selector_after_Take_causes_pushdown()
{
	var result = MyClass1Query.Query().Take(2).Average(m => m.Prop2);

	Assert.AreEqual(
			@"SELECT AVG(CAST([m].[Prop2] AS float))
FROM (
    SELECT TOP (2) [m_0].[Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
) AS [m]",
			SqlLog);
}

[TestMethod]
public void Average_with_selector_after_GroupBy_causes_pushdown()
{
	var result = MyClass1Query.Query().GroupBy(m => m.Prop2).Average(g => g.Key);

	Assert.AreEqual(
			@"SELECT AVG(CAST([g].[Prop2] AS float))
FROM (
    SELECT [g_0].[Prop2]
    FROM [dbo].[MyClass1] AS [g_0]
    GROUP BY [g_0].[Prop2]
) AS [g]",
			SqlLog);
}

[TestMethod]
public void Count_after_Distinct_causes_pushdown()
{
	var result = MyClass1Query.Query().Select(m => m.Prop2).Distinct().Count();

	Assert.AreEqual(
			@"SELECT COUNT(*)
FROM (
    SELECT DISTINCT [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Count_after_Skip_causes_pushdown()
{
	var result = MyClass1Query.Query().Select(m => m.Prop2).Skip(1).Count();

	Assert.AreEqual(
			@"SELECT COUNT(*)
FROM (
    SELECT [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
    ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
    OFFSET 1 ROWS
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Count_after_Take_causes_pushdown()
{
	var result = MyClass1Query.Query().Select(m => m.Prop2).Take(2).Count();

	Assert.AreEqual(
			@"SELECT COUNT(*)
FROM (
    SELECT TOP (2) [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Count_after_GroupBy_causes_pushdown()
{
	var result = MyClass1Query.Query().GroupBy(m => m.Prop2).Select(g => g.Key).Count();

	Assert.AreEqual(
			@"SELECT COUNT(*)
FROM (
    SELECT [g].[Prop2]
    FROM [dbo].[MyClass1] AS [g]
    GROUP BY [g].[Prop2]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Count_with_predicate_after_Distinct_causes_pushdown()
{
	var result = MyClass1Query.Query().Distinct().Count(m => m.Prop2 == 77);

	Assert.AreEqual(
			@"SELECT COUNT(*)
FROM (
    SELECT DISTINCT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [m_0]
WHERE [m_0].[Prop2] = 77",
			SqlLog);
}

[TestMethod]
public void Count_with_predicate_after_Skip_causes_pushdown()
{
	var result = MyClass1Query.Query().Skip(1).Count(m => m.Prop2 == 77);

	Assert.AreEqual(
			@"SELECT COUNT(*)
FROM (
    SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
    ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
    OFFSET 1 ROWS
) AS [m_0]
WHERE [m_0].[Prop2] = 77",
			SqlLog);
}

[TestMethod]
public void Count_with_predicate_after_Take_causes_pushdown()
{
	var result = MyClass1Query.Query().Take(2).Count(m => m.Prop2 == 77);

	Assert.AreEqual(
			@"SELECT COUNT(*)
FROM (
    SELECT TOP (2) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [m_0]
WHERE [m_0].[Prop2] = 77",
			SqlLog);
}

[TestMethod]
public void Count_with_predicate_after_GroupBy_causes_pushdown()
{
	var result = MyClass1Query.Query().GroupBy(m => m.Prop2).Count(g => g.Key == 77);

	Assert.AreEqual(
			@"SELECT COUNT(*)
FROM (
    SELECT [g].[Prop2] AS [Key]
    FROM [dbo].[MyClass1] AS [g]
    GROUP BY [g].[Prop2]
) AS [g_0]
WHERE [g_0].[Key] = 77",
			SqlLog);
}

[TestMethod]
public void LongCount_after_Distinct_causes_pushdown()
{
	var result = MyClass1Query.Query().Select(m => m.Prop2).Distinct().LongCount();

	Assert.AreEqual(
			@"SELECT COUNT_BIG(*)
FROM (
    SELECT DISTINCT [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void LongCount_after_Skip_causes_pushdown()
{
	var result = MyClass1Query.Query().Select(m => m.Prop2).Skip(1).LongCount();

	Assert.AreEqual(
			@"SELECT COUNT_BIG(*)
FROM (
    SELECT [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
    ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
    OFFSET 1 ROWS
) AS [t]",
			SqlLog);
}

[TestMethod]
public void LongCount_after_Take_causes_pushdown()
{
	var result = MyClass1Query.Query().Select(m => m.Prop2).Take(2).LongCount();

	Assert.AreEqual(
			@"SELECT COUNT_BIG(*)
FROM (
    SELECT TOP (2) [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void LongCount_after_GroupBy_causes_pushdown()
{
	var result = MyClass1Query.Query().GroupBy(m => m.Prop2).Select(g => g.Key).LongCount();

	Assert.AreEqual(
			@"SELECT COUNT_BIG(*)
FROM (
    SELECT [g].[Prop2]
    FROM [dbo].[MyClass1] AS [g]
    GROUP BY [g].[Prop2]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void LongCount_with_predicate_after_Distinct_causes_pushdown()
{
	var result = MyClass1Query.Query().Distinct().LongCount(m => m.Prop2 == 77);

	Assert.AreEqual(
			@"SELECT COUNT_BIG(*)
FROM (
    SELECT DISTINCT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [m_0]
WHERE [m_0].[Prop2] = 77",
			SqlLog);
}

[TestMethod]
public void LongCount_with_predicate_after_Skip_causes_pushdown()
{
	var result = MyClass1Query.Query().Skip(1).LongCount(m => m.Prop2 == 77);

	Assert.AreEqual(
			@"SELECT COUNT_BIG(*)
FROM (
    SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
    ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
    OFFSET 1 ROWS
) AS [m_0]
WHERE [m_0].[Prop2] = 77",
			SqlLog);
}

[TestMethod]
public void LongCount_with_predicate_after_Take_causes_pushdown()
{
	var result = MyClass1Query.Query().Take(2).LongCount(m => m.Prop2 == 77);

	Assert.AreEqual(
			@"SELECT COUNT_BIG(*)
FROM (
    SELECT TOP (2) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [m_0]
WHERE [m_0].[Prop2] = 77",
			SqlLog);
}

[TestMethod]
public void LongCount_with_predicate_after_GroupBy_causes_pushdown()
{
	var result = MyClass1Query.Query().GroupBy(m => m.Prop2).LongCount(g => g.Key == 77);

	Assert.AreEqual(
			@"SELECT COUNT_BIG(*)
FROM (
    SELECT [g].[Prop2] AS [Key]
    FROM [dbo].[MyClass1] AS [g]
    GROUP BY [g].[Prop2]
) AS [g_0]
WHERE [g_0].[Key] = 77",
			SqlLog);
}

[TestMethod]
public void Max_after_Distinct_causes_pushdown()
{
	var result = MyClass1Query.Query().Select(m => m.Prop2).Distinct().Max();

	Assert.AreEqual(
			@"SELECT MAX([t].[Prop2])
FROM (
    SELECT DISTINCT [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Max_after_Skip_causes_pushdown()
{
	var result = MyClass1Query.Query().Select(m => m.Prop2).Skip(1).Max();

	Assert.AreEqual(
			@"SELECT MAX([t].[Prop2])
FROM (
    SELECT [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
    ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
    OFFSET 1 ROWS
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Max_after_Take_causes_pushdown()
{
	var result = MyClass1Query.Query().Select(m => m.Prop2).Take(2).Max();

	Assert.AreEqual(
			@"SELECT MAX([t].[Prop2])
FROM (
    SELECT TOP (2) [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Max_after_GroupBy_causes_pushdown()
{
	var result = MyClass1Query.Query().GroupBy(m => m.Prop2).Select(g => g.Key).Max();

	Assert.AreEqual(
			@"SELECT MAX([t].[Prop2])
FROM (
    SELECT [g].[Prop2]
    FROM [dbo].[MyClass1] AS [g]
    GROUP BY [g].[Prop2]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Max_with_selector_after_Distinct_causes_pushdown()
{
	var result = MyClass1Query.Query().Distinct().Max(m => m.Prop2);

	Assert.AreEqual(
			@"SELECT MAX([m].[Prop2])
FROM (
    SELECT DISTINCT [m_0].[Prop1] AS [Prop1], [m_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
) AS [m]",
			SqlLog);
}

[TestMethod]
public void Max_with_selector_after_Skip_causes_pushdown()
{
	var result = MyClass1Query.Query().Skip(1).Max(m => m.Prop2);

	Assert.AreEqual(
			@"SELECT MAX([m].[Prop2])
FROM (
    SELECT [m_0].[Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
    ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
    OFFSET 1 ROWS
) AS [m]",
			SqlLog);
}

[TestMethod]
public void Max_with_selector_after_Take_causes_pushdown()
{
	var result = MyClass1Query.Query().Take(2).Max(m => m.Prop2);

	Assert.AreEqual(
			@"SELECT MAX([m].[Prop2])
FROM (
    SELECT TOP (2) [m_0].[Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
) AS [m]",
			SqlLog);
}

[TestMethod]
public void Max_with_selector_after_GroupBy_causes_pushdown()
{
	var result = MyClass1Query.Query().GroupBy(m => m.Prop2).Max(g => g.Key);

	Assert.AreEqual(
			@"SELECT MAX([g].[Prop2])
FROM (
    SELECT [g_0].[Prop2]
    FROM [dbo].[MyClass1] AS [g_0]
    GROUP BY [g_0].[Prop2]
) AS [g]",
			SqlLog);
}

[TestMethod]
public void Min_after_Distinct_causes_pushdown()
{
	var result = MyClass1Query.Query().Select(m => m.Prop2).Distinct().Min();

	Assert.AreEqual(
			@"SELECT MIN([t].[Prop2])
FROM (
    SELECT DISTINCT [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Min_after_Skip_causes_pushdown()
{
	var result = MyClass1Query.Query().Select(m => m.Prop2).Skip(1).Min();

	Assert.AreEqual(
			@"SELECT MIN([t].[Prop2])
FROM (
    SELECT [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
    ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
    OFFSET 1 ROWS
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Min_after_Take_causes_pushdown()
{
	var result = MyClass1Query.Query().Select(m => m.Prop2).Take(2).Min();

	Assert.AreEqual(
			@"SELECT MIN([t].[Prop2])
FROM (
    SELECT TOP (2) [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Min_after_GroupBy_causes_pushdown()
{
	var result = MyClass1Query.Query().GroupBy(m => m.Prop2).Select(g => g.Key).Min();

	Assert.AreEqual(
			@"SELECT MIN([t].[Prop2])
FROM (
    SELECT [g].[Prop2]
    FROM [dbo].[MyClass1] AS [g]
    GROUP BY [g].[Prop2]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Min_with_selector_after_Distinct_causes_pushdown()
{
	var result = MyClass1Query.Query().Distinct().Min(m => m.Prop2);

	Assert.AreEqual(
			@"SELECT MIN([m].[Prop2])
FROM (
    SELECT DISTINCT [m_0].[Prop1] AS [Prop1], [m_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
) AS [m]",
			SqlLog);
}

[TestMethod]
public void Min_with_selector_after_Skip_causes_pushdown()
{
	var result = MyClass1Query.Query().Skip(1).Min(m => m.Prop2);

	Assert.AreEqual(
			@"SELECT MIN([m].[Prop2])
FROM (
    SELECT [m_0].[Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
    ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
    OFFSET 1 ROWS
) AS [m]",
			SqlLog);
}

[TestMethod]
public void Min_with_selector_after_Take_causes_pushdown()
{
	var result = MyClass1Query.Query().Take(2).Min(m => m.Prop2);

	Assert.AreEqual(
			@"SELECT MIN([m].[Prop2])
FROM (
    SELECT TOP (2) [m_0].[Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
) AS [m]",
			SqlLog);
}

[TestMethod]
public void Min_with_selector_after_GroupBy_causes_pushdown()
{
	var result = MyClass1Query.Query().GroupBy(m => m.Prop2).Min(g => g.Key);

	Assert.AreEqual(
			@"SELECT MIN([g].[Prop2])
FROM (
    SELECT [g_0].[Prop2]
    FROM [dbo].[MyClass1] AS [g_0]
    GROUP BY [g_0].[Prop2]
) AS [g]",
			SqlLog);
}

[TestMethod]
public void Sum_after_Distinct_causes_pushdown()
{
	var result = MyClass1Query.Query().Select(m => m.Prop2).Distinct().Sum();

	Assert.AreEqual(
			@"SELECT SUM([t].[Prop2])
FROM (
    SELECT DISTINCT [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Sum_after_Skip_causes_pushdown()
{
	var result = MyClass1Query.Query().Select(m => m.Prop2).Skip(1).Sum();

	Assert.AreEqual(
			@"SELECT SUM([t].[Prop2])
FROM (
    SELECT [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
    ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
    OFFSET 1 ROWS
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Sum_after_Take_causes_pushdown()
{
	var result = MyClass1Query.Query().Select(m => m.Prop2).Take(2).Sum();

	Assert.AreEqual(
			@"SELECT SUM([t].[Prop2])
FROM (
    SELECT TOP (2) [m].[Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Sum_after_GroupBy_causes_pushdown()
{
	var result = MyClass1Query.Query().GroupBy(m => m.Prop2).Select(g => g.Key).Sum();

	Assert.AreEqual(
			@"SELECT SUM([t].[Prop2])
FROM (
    SELECT [g].[Prop2]
    FROM [dbo].[MyClass1] AS [g]
    GROUP BY [g].[Prop2]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Sum_with_selector_after_Distinct_causes_pushdown()
{
	var result = MyClass1Query.Query().Distinct().Sum(m => m.Prop2);

	Assert.AreEqual(
			@"SELECT SUM([m].[Prop2])
FROM (
    SELECT DISTINCT [m_0].[Prop1] AS [Prop1], [m_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
) AS [m]",
			SqlLog);
}

[TestMethod]
public void Sum_with_selector_after_Skip_causes_pushdown()
{
	var result = MyClass1Query.Query().Skip(1).Sum(m => m.Prop2);

	Assert.AreEqual(
			@"SELECT SUM([m].[Prop2])
FROM (
    SELECT [m_0].[Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
    ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
    OFFSET 1 ROWS
) AS [m]",
			SqlLog);
}

[TestMethod]
public void Sum_with_selector_after_Take_causes_pushdown()
{
	var result = MyClass1Query.Query().Take(2).Sum(m => m.Prop2);

	Assert.AreEqual(
			@"SELECT SUM([m].[Prop2])
FROM (
    SELECT TOP (2) [m_0].[Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
) AS [m]",
			SqlLog);
}

[TestMethod]
public void Sum_with_selector_after_GroupBy_causes_pushdown()
{
	var result = MyClass1Query.Query().GroupBy(m => m.Prop2).Sum(g => g.Key);

	Assert.AreEqual(
			@"SELECT SUM([g].[Prop2])
FROM (
    SELECT [g_0].[Prop2]
    FROM [dbo].[MyClass1] AS [g_0]
    GROUP BY [g_0].[Prop2]
) AS [g]",
			SqlLog);
}

[TestMethod]
public void Where_after_Distinct_causes_pushdown()
{
	var result = MyClass1Query.Query().Distinct().Where(m => m.Prop2 == 77).ToList();

	Assert.AreEqual(
			@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM (
    SELECT DISTINCT [m_0].[Prop1] AS [Prop1], [m_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
) AS [m]
WHERE [m].[Prop2] = 77",
			SqlLog);
}

[TestMethod]
public void Where_after_Skip_causes_pushdown()
{
	var result = MyClass1Query.Query().Skip(1).Where(m => m.Prop2 == 77).ToList();

	Assert.AreEqual(
			@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM (
    SELECT [m_0].[Prop1] AS [Prop1], [m_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
    ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
    OFFSET 1 ROWS
) AS [m]
WHERE [m].[Prop2] = 77",
			SqlLog);
}

[TestMethod]
public void Where_after_Take_causes_pushdown()
{
	var result = MyClass1Query.Query().Take(2).Where(m => m.Prop2 == 77).ToList();

	Assert.AreEqual(
			@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM (
    SELECT TOP (2) [m_0].[Prop1] AS [Prop1], [m_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
) AS [m]
WHERE [m].[Prop2] = 77",
			SqlLog);
}

[TestMethod]
public void Where_after_GroupBy_causes_pushdown()
{
	var result
			= MyClass1Query.Query()
					.GroupBy(m => m.Prop2)
					.Where(g => g.Key == 77)
					.Select(g => g.Key)
					.ToList();

	Assert.AreEqual(
			@"SELECT [g].[Key]
FROM (
    SELECT [g_0].[Prop2] AS [Key]
    FROM [dbo].[MyClass1] AS [g_0]
    GROUP BY [g_0].[Prop2]
) AS [g]
WHERE [g].[Key] = 77",
			SqlLog);
}

[TestMethod]
public void Select_after_Distinct_causes_pushdown()
{
	var result = MyClass1Query.Query().Distinct().Select(m => m.Prop2).ToList();

	Assert.AreEqual(
			@"SELECT [m].[Prop2]
FROM (
    SELECT DISTINCT [m_0].[Prop1] AS [Prop1], [m_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
) AS [m]",
			SqlLog);
}

[TestMethod]
public void Distinct_after_Skip_causes_pushdown()
{
	var result = MyClass1Query.Query().Skip(1).Distinct().ToList();

	Assert.AreEqual(
			@"SELECT DISTINCT [t].[Prop1] AS [Prop1], [t].[Prop2] AS [Prop2]
FROM (
    SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
    ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
    OFFSET 1 ROWS
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Distinct_after_Take_causes_pushdown()
{
	var result = MyClass1Query.Query().Take(2).Distinct().ToList();

	Assert.AreEqual(
			@"SELECT DISTINCT [t].[Prop1] AS [Prop1], [t].[Prop2] AS [Prop2]
FROM (
    SELECT TOP (2) [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m]
) AS [t]",
			SqlLog);
}

[TestMethod]
public void Distinct_after_OrderBy_drops_ordering()
{
	var result = MyClass1Query.Query().OrderBy(m => m.Prop2 + m.Prop2).Distinct().ToList();

	Assert.AreEqual(
			@"SELECT DISTINCT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]",
			SqlLog);
}

[TestMethod]
public void OrderBy_after_Distinct_causes_pushdown()
{
	var result = MyClass1Query.Query().Distinct().OrderBy(m => m.Prop2).ToList();

	Assert.AreEqual(
			@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM (
    SELECT DISTINCT [m_0].[Prop1] AS [Prop1], [m_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
) AS [m]
ORDER BY [m].[Prop2] ASC",
			SqlLog);
}

[TestMethod]
public void OrderBy_after_Skip_causes_pushdown()
{
	var result = MyClass1Query.Query().Skip(1).OrderBy(m => m.Prop2).ToList();

	Assert.AreEqual(
			@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM (
    SELECT [m_0].[Prop1] AS [Prop1], [m_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
    ORDER BY ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) ASC
    OFFSET 1 ROWS
) AS [m]
ORDER BY [m].[Prop2] ASC",
			SqlLog);
}

[TestMethod]
public void OrderBy_after_Take_causes_pushdown()
{
	var result = MyClass1Query.Query().Take(2).OrderBy(m => m.Prop2).ToList();

	Assert.AreEqual(
			@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM (
    SELECT TOP (2) [m_0].[Prop1] AS [Prop1], [m_0].[Prop2] AS [Prop2]
    FROM [dbo].[MyClass1] AS [m_0]
) AS [m]
ORDER BY [m].[Prop2] ASC",
			SqlLog);
}

[TestMethod]
public void LeftJoin_nested_right_side()
{
	var query = from m1a in MyClass1Query.Query()
							join x in (from m1b in MyClass1Query.Query()
												 join m2 in MyClass2Query.Query()
												 on m1b.Prop2 equals m2.Prop2 into m2g
							from m2 in m2g.DefaultIfEmpty()
							select new { m1b, m2 })
                                   on m1a.Prop2 equals x.m1b.Prop2 into xg
												from x in xg.DefaultIfEmpty()

												select new { m1a, x };

	query.ToList();

	Assert.AreEqual(
			@"SELECT [m1a].[Prop1] AS [m1a.Prop1], [m1a].[Prop2] AS [m1a.Prop2], [x].[$empty] AS [x.$empty], [x].[m1b.Prop1] AS [x.m1b.Prop1], [x].[m1b.Prop2] AS [x.m1b.Prop2], [x].[m2.$empty] AS [x.m2.$empty], [x].[m2.Prop1] AS [x.m2.Prop1], [x].[m2.Prop2] AS [x.m2.Prop2]
FROM [dbo].[MyClass1] AS [m1a]
LEFT JOIN (
    SELECT 0 AS [$empty], [m1b].[Prop1] AS [m1b.Prop1], [m1b].[Prop2] AS [m1b.Prop2], [m2].[$empty] AS [m2.$empty], [m2].[Prop1] AS [m2.Prop1], [m2].[Prop2] AS [m2.Prop2]
    FROM [dbo].[MyClass1] AS [m1b]
    LEFT JOIN (
        SELECT 0 AS [$empty], [m2_0].[Prop1] AS [Prop1], [m2_0].[Prop2] AS [Prop2]
        FROM [dbo].[MyClass2] AS [m2_0]
    ) AS [m2] ON [m1b].[Prop2] = [m2].[Prop2]
) AS [x] ON [m1a].[Prop2] = [x].[m1b.Prop2]",
			SqlLog);
}

[TestMethod]
public void LeftJoin_nested_left_side()
{
	var query = from x in (from m1b in MyClass1Query.Query()
												 join m2 in MyClass2Query.Query()

																	 on m1b.Prop2 equals m2.Prop2 into m2g
																	 from m2 in m2g.DefaultIfEmpty()

																	 select new { m1b, m2 }).Take(10)

												join m1a in MyClass1Query.Query()

														on x.m1b.Prop2 equals m1a.Prop2
												select new { m1a, x };

	query.ToList();

	Assert.AreEqual(
			@"SELECT [m1a].[Prop1] AS [m1a.Prop1], [m1a].[Prop2] AS [m1a.Prop2], [x].[m1b.Prop1] AS [x.m1b.Prop1], [x].[m1b.Prop2] AS [x.m1b.Prop2], [x].[m2.$empty] AS [x.m2.$empty], [x].[m2.Prop1] AS [x.m2.Prop1], [x].[m2.Prop2] AS [x.m2.Prop2]
FROM (
    SELECT TOP (10) [m1b].[Prop1] AS [m1b.Prop1], [m1b].[Prop2] AS [m1b.Prop2], [m2].[$empty] AS [m2.$empty], [m2].[Prop1] AS [m2.Prop1], [m2].[Prop2] AS [m2.Prop2]
    FROM [dbo].[MyClass1] AS [m1b]
    LEFT JOIN (
        SELECT 0 AS [$empty], [m2_0].[Prop1] AS [Prop1], [m2_0].[Prop2] AS [Prop2]
        FROM [dbo].[MyClass2] AS [m2_0]
    ) AS [m2] ON [m1b].[Prop2] = [m2].[Prop2]
) AS [x]
INNER JOIN [dbo].[MyClass1] AS [m1a] ON [x].[m1b.Prop2] = [m1a].[Prop2]",
			SqlLog);
}

[TestMethod]
public void Weird_Query()
{
	var query = from q in (from x in MyClass1Query.Query()
												 from zs in (from y in MyClass2Query.Query()
												 select MyClass2Query.Query().Where(z => z.Prop2 == x.Prop2))
                                   select new { x, zs }).Take(10)

												from z in q.zs
												select new { q.x, z };

	query.ToList();

	Assert.AreEqual(
			@"SELECT TOP (10) [x].[Prop1] AS [x.Prop1], [x].[Prop2] AS [x.Prop2], [zs].[$c] AS [zs]
FROM [dbo].[MyClass1] AS [x]
CROSS APPLY (
    SELECT (
        SELECT [z].[Prop1] AS [Prop1], [z].[Prop2] AS [Prop2]
        FROM [dbo].[MyClass2] AS [z]
        WHERE [z].[Prop2] = [x].[Prop2]
        FOR JSON PATH, INCLUDE_NULL_VALUES
    ) AS [$c]
    FROM [dbo].[MyClass2] AS [y]
) AS [zs]",
			SqlLog);
}

[TestMethod]
public void NonLambdaPredicate()
{
	var query = MyClass1Query.Query().Where(Drop);

	query.ToList();

	Assert.AreEqual(
			@"SELECT [m].[Prop1] AS [Prop1], [m].[Prop2] AS [Prop2]
FROM [dbo].[MyClass1] AS [m]",
			SqlLog);
}

private static bool Drop<T>(T arg)
{
	return false;
}

[TestMethod]
public void Zip_simple()
{
	var m1s = MyClass1Query.Query();
	var m2s = MyClass2Query.Query();

	var query = m1s.Zip(m2s, (m1, m2) => new { m1, m2 });

	query.ToList();

	Assert.AreEqual(
			@"SELECT [m1].[Prop1] AS [m1.Prop1], [m1].[Prop2] AS [m1.Prop2], [m2].[Prop1] AS [m2.Prop1], [m2].[Prop2] AS [m2.Prop2]
FROM (
    SELECT [m1_0].[Prop1] AS [Prop1], [m1_0].[Prop2] AS [Prop2], ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) AS [$rownumber]
    FROM [dbo].[MyClass1] AS [m1_0]
) AS [m1]
INNER JOIN (
    SELECT [m2_0].[Prop1] AS [Prop1], [m2_0].[Prop2] AS [Prop2], ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) AS [$rownumber]
    FROM [dbo].[MyClass2] AS [m2_0]
) AS [m2] ON [m1].[$rownumber] = [m2].[$rownumber]",
			SqlLog);
}

[TestMethod]
public void SequenceEqual_simple()
{
	var m1s = MyClass1Query.Query().Select(m1 => new { m1.Prop1, m1.Prop2 });
	var m2s = MyClass2Query.Query().Select(m2 => new { m2.Prop1, m2.Prop2 });

	var result = m1s.SequenceEqual(m2s);

	Assert.IsTrue(result);

	Assert.AreEqual(
			@"SELECT CAST((CASE WHEN NOT EXISTS (
    SELECT 1
    FROM (
        SELECT [m1].[Prop1] AS [Prop1], [m1].[Prop2] AS [Prop2], ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) AS [$rownumber]
        FROM [dbo].[MyClass1] AS [m1]
    ) AS [t]
    FULL JOIN (
        SELECT [m2].[Prop1] AS [Prop1], [m2].[Prop2] AS [Prop2], ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) AS [$rownumber]
        FROM [dbo].[MyClass2] AS [m2]
    ) AS [t_0] ON [t].[$rownumber] = [t_0].[$rownumber]
    WHERE (([t].[$rownumber] IS NULL) OR ([t_0].[$rownumber] IS NULL)) OR (([t].[Prop1] <> [t_0].[Prop1]) OR ([t].[Prop2] <> [t_0].[Prop2]))
) THEN 1 ELSE 0 END) AS BIT)",
			SqlLog);
}

[TestMethod]
public void SkipWhile_without_index()
{
	var query
			= MyClass1Query.Query()
					.SkipWhile(m1 => m1.Prop2 < 8);

	query.ToList();

	Assert.AreEqual(
			@"SELECT [m1].[Prop1] AS [Prop1], [m1].[Prop2] AS [Prop2]
FROM (
    SELECT [m1_0].[Prop1] AS [Prop1], [m1_0].[Prop2] AS [Prop2], ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) AS [$rownumber]
    FROM [dbo].[MyClass1] AS [m1_0]
) AS [m1]
WHERE [m1].[$rownumber] >= (
    SELECT COALESCE(MIN([m1_1].[$rownumber]), 0)
    FROM (
        SELECT [m1_2].[Prop1] AS [Prop1], [m1_2].[Prop2] AS [Prop2], ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) AS [$rownumber]
        FROM [dbo].[MyClass1] AS [m1_2]
    ) AS [m1_1]
    WHERE [m1_1].[Prop2] >= 8
)",
			SqlLog);
}

[TestMethod]
public void SkipWhile_with_index()
{
	var query
			= MyClass1Query.Query()
					.SkipWhile((m1, i) => i <= 1 || m1.Prop2 < 8);

	query.ToList();

	Assert.AreEqual(
			@"SELECT [m1].[Prop1] AS [Prop1], [m1].[Prop2] AS [Prop2]
FROM (
    SELECT [m1_0].[Prop1] AS [Prop1], [m1_0].[Prop2] AS [Prop2], ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) AS [$rownumber]
    FROM [dbo].[MyClass1] AS [m1_0]
) AS [m1]
WHERE [m1].[$rownumber] >= (
    SELECT COALESCE(MIN([m1_1].[$rownumber]), 0)
    FROM (
        SELECT [m1_2].[Prop1] AS [Prop1], [m1_2].[Prop2] AS [Prop2], ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) AS [$rownumber]
        FROM [dbo].[MyClass1] AS [m1_2]
    ) AS [m1_1]
    WHERE (([m1_1].[$rownumber] - 1) > 1) AND ([m1_1].[Prop2] >= 8)
)",
			SqlLog);
}

[TestMethod]
public void TakeWhile_without_index()
{
	var query
			= MyClass1Query.Query()
					.TakeWhile(m1 => m1.Prop2 > 8);

	query.ToList();

	Assert.AreEqual(
			@"SELECT [m1].[Prop1] AS [Prop1], [m1].[Prop2] AS [Prop2]
FROM (
    SELECT [m1_0].[Prop1] AS [Prop1], [m1_0].[Prop2] AS [Prop2], ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) AS [$rownumber]
    FROM [dbo].[MyClass1] AS [m1_0]
) AS [m1]
WHERE [m1].[$rownumber] < (
    SELECT COALESCE(MIN([m1_1].[$rownumber]), [m1].[$rownumber] + 1)
    FROM (
        SELECT [m1_2].[Prop1] AS [Prop1], [m1_2].[Prop2] AS [Prop2], ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) AS [$rownumber]
        FROM [dbo].[MyClass1] AS [m1_2]
    ) AS [m1_1]
    WHERE [m1_1].[Prop2] <= 8
)",
			SqlLog);
}

[TestMethod]
public void TakeWhile_with_index()
{
	var query
			= MyClass1Query.Query()
					.TakeWhile((m1, i) => i <= 8 || m1.Prop2 > 8);

	query.ToList();

	Assert.AreEqual(
			@"SELECT [m1].[Prop1] AS [Prop1], [m1].[Prop2] AS [Prop2]
FROM (
    SELECT [m1_0].[Prop1] AS [Prop1], [m1_0].[Prop2] AS [Prop2], ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) AS [$rownumber]
    FROM [dbo].[MyClass1] AS [m1_0]
) AS [m1]
WHERE [m1].[$rownumber] < (
    SELECT COALESCE(MIN([m1_1].[$rownumber]), [m1].[$rownumber] + 1)
    FROM (
        SELECT [m1_2].[Prop1] AS [Prop1], [m1_2].[Prop2] AS [Prop2], ROW_NUMBER() OVER(ORDER BY (SELECT 1) ASC) AS [$rownumber]
        FROM [dbo].[MyClass1] AS [m1_2]
    ) AS [m1_1]
    WHERE (([m1_1].[$rownumber] - 1) > 8) AND ([m1_1].[Prop2] <= 8)
)",
			SqlLog);
}

[TestMethod]
public void Complex_Nested_Query_sequence_of_scalar_values_ToList()
{
	var query =
			from a in MyClass1Query.Query()
			select (from b in MyClass2Query.Query()
							select b.Prop2).ToList();

	query.ToList();

	Assert.AreEqual(
			@"SELECT (
    SELECT [b].[Prop2]
    FROM [dbo].[MyClass2] AS [b]
    FOR JSON PATH, INCLUDE_NULL_VALUES
)
FROM [dbo].[MyClass1] AS [a]",
			SqlLog);
}

[TestMethod]
public void Complex_Nested_Query_sequence_of_scalar_values_ToArray()
{
	var query =
			from a in MyClass1Query.Query()
			select (from b in MyClass2Query.Query()
							select b.Prop2).ToArray();

	query.ToList();

	Assert.AreEqual(
			@"SELECT (
    SELECT [b].[Prop2]
    FROM [dbo].[MyClass2] AS [b]
    FOR JSON PATH, INCLUDE_NULL_VALUES
)
FROM [dbo].[MyClass1] AS [a]",
			SqlLog);
}

private class TestImpatientConnectionFactory : IImpatientDbConnectionFactory
{
	private readonly string connectionString;

	public TestImpatientConnectionFactory()
	{
		connectionString = @"Server=.\sqlexpress; Database=Impatient; Trusted_Connection=True";
	}

	public TestImpatientConnectionFactory(string connectionString)
	{
		this.connectionString = connectionString;
	}

	public DbConnection CreateConnection()
	{
		return new SqlConnection(connectionString);
	}
}

private class MyKeyObject
{
	public string Prop1 { get; set; }

	public int Prop2 { get; set; }
}

private class MyClass1
{
	public MyClass1()
	{
	}

	public MyClass1(string prop1, int prop2)
	{
		Prop1 = prop1;
		Prop2 = prop2;
	}

	public string Prop1 { get; set; }

	public int Prop2 { get; set; }

	public short Unmapped { get; set; }
}

private class MyClass2
{
	public string Prop1 { get; set; }

	public int Prop2 { get; set; }
}
    }
}
