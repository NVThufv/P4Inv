#ifndef __SORCAR_H__
#define __SORCAR_H__

// C++ includes
#include <fstream>
#include <iostream>
#include <list>
#include <map>
#include <set>
#include <stdexcept>
#include <vector>

// C includes
#include <cassert>

// Z3 includes
#include "z3++.h"

// Project includes
#include "attributes_metadata.h"
#include "datapoint.h"
#include "horn_constraint.h"

namespace horn_verification
{

	class sorcar
	{
		
		
		public:

		/**
		 * Checks whether a data point satisfies a conjunction.
		 *
		 * @param dp the data point
		 * @param conjunction the conjunction
		 *
		 * @return true if the data point satisfies the conjunction and false otherwise
		 */
		static bool satisfies(const datapoint<bool> & dp, const std::set<unsigned> & conjunction)
		{

			for (const auto & c : conjunction)
			{
				if (!dp._int_data[c])
				{
					return false;
				}
			}

			return true;

		}



		static std::vector<std::set<unsigned>> horndini(const std::vector<datapoint<bool>> & datapoints, const std::vector<horn_constraint<bool>> & horn_constraints, const std::vector<std::pair<unsigned, unsigned>> & intervals)
		{
			
			//
			// Check arguments
			//
			if (intervals.empty())
			{
				throw std::runtime_error("Intervals are empty");
			}
			
			
			//
			// Prepare initial conjunctions
			//
			std::vector<std::set<unsigned>> conjunctions(intervals.size());
			for (unsigned i = 0; i < intervals.size(); ++i)
			{
				
				auto & conjunction = conjunctions[i];

				for (unsigned j = intervals[i].first; j <= intervals[i].second; ++j)
				{
					conjunction.insert(conjunction.end(), j);
				}
				
			}
			
			
			//
			// Run Horndini
			//
			horndini(datapoints, horn_constraints, conjunctions);
			
			//
			// Return
			//
			return conjunctions;
			
		}
		

		static void horndini(const std::vector<datapoint<bool>> & datapoints, const std::vector<horn_constraint<bool>> & horn_constraints, std::vector<std::set<unsigned>> & conjunctions)
		{
			
			//
			// Create list of positive data points
			//
			std::vector<const datapoint<bool> *> positive;
			positive.reserve(datapoints.size());
			for (const auto & dp : datapoints)
			{
				if (dp._is_classified && dp._classification)
				{
					positive.push_back(&dp);
				}
			}

			
			//
			// Working copy of Horn constraints
			//
			std::list<horn_constraint<bool>> copy_of_hc;
			for (const auto & hc : horn_constraints)
			{
				
				// Copy left-hand-side
				std::vector <datapoint<bool> *> lhs = hc._premises;

				// Add Horn constraint
				copy_of_hc.push_back(horn_constraint<bool>(lhs, hc._conclusion));
				
			}
			
			
			//
			// Knock out predicates that occur negative in a positive datapoint
			//
			do
			{

				// Process positive datapoints
				for (const auto & dp : positive)
				{
				
					auto id = dp->_categorical_data[0];
					assert (id >= 0 && id < conjunctions.size());
				
					auto pred_it = conjunctions[id].begin();
					while (pred_it != conjunctions[id].end())
					{
				
						if (!dp->_int_data[*pred_it])
						{
							pred_it = conjunctions[id].erase(pred_it);
						}
						else
						{
							++pred_it;
						}
				
					}
				
				}
				
				positive.clear();
				
				
				// Add right-hand-side of Horn clause if all left-hand-sides satisfy the current conjunction
				auto horn_it = copy_of_hc.begin();
				while (horn_it != copy_of_hc.end())
				{
				
					// Check whether data point of left-hand-side is satisfied
					auto lhs_it = horn_it->_premises.begin();
					while (lhs_it != horn_it->_premises.end())
					{
						
						auto id = (*lhs_it)->_categorical_data[0];
						assert (id >= 0 && id < conjunctions.size());
						
						// Remove data point from lhs if it satisfies the conjunction
						if (satisfies(**lhs_it, conjunctions[id]))
						{
							lhs_it = horn_it->_premises.erase(lhs_it);
						}
						else
						{
							++lhs_it;
						}
						
					}
				
					// If left-hand-side is empty, add right-hand-side to positive
					if (horn_it->_premises.empty())
					{
						
						if (horn_it->_conclusion == nullptr)
						{
							throw std::runtime_error("No consistent conjunction exists");
						}
						else
						{
							positive.push_back(horn_it->_conclusion);
							horn_it = copy_of_hc.erase(horn_it);
						}
						
					}
					else
					{
						++ horn_it;
					}
				
				}
				
			}
			while (!positive.empty());

		}

		
		
		static void reduce_predicates_all(const std::vector<datapoint<bool>> & datapoints, const std::vector<horn_constraint<bool>> & horn_constraints, const std::vector<std::set<unsigned>> & X, std::vector<std::set<unsigned>> & R)
		{
			
			//
			// Check arguments
			//
			if (X.empty())
			{
				throw std::runtime_error("X must not be empty");
			}
			else if (X.size() != R.size())
			{
				throw std::runtime_error("R and X must be of same size");
			}
			
			
			//
			// R = R \cap X
			// X_minus_R = X \ R
			//
			std::vector<std::set<unsigned>> X_minus_R(X.size());
			prepare_sets(X, R, X_minus_R);


			//
			// Process negative examples
			//
			for (const auto & dp : datapoints)
			{

				auto id = dp._categorical_data[0];
				assert (id >= 0 && id < R.size());
		
				// If negative example satisfies the current conjunction, add all its 0-entries to the conjunction
				if (dp._is_classified && !dp._classification && satisfies(dp, R[id]))
				{
					
					if (!satisfies(dp, X[id]))
					{
						std::cout << dp << std::endl << std::flush;
					}
					assert(!satisfies(dp, X[id]));
					
					auto size_before = R[id].size();
					auto it_XmR = X_minus_R[id].begin();
					
					while (it_XmR != X_minus_R[id].end())
					{

						if (dp._int_data[*it_XmR] == 0)
						{
							R[id].insert(*it_XmR);
							it_XmR = X_minus_R[id].erase(it_XmR);
						}
						else
						{
							++it_XmR;
						}
						
					}
					
					assert (size_before < R[id].size());
					
				}
		
			}
			
			
			//
			// Process Horn constraints
			//
			
			// Create observer pointers to Horn constraints
			std::list<const horn_constraint<bool> *> horn_constraint_pointers;
			for (const auto & hc : horn_constraints)
			{
				horn_constraint_pointers.push_back(&hc);
			}
			
			// Do fixed-point computation until all Horn constraints satisfy R
			bool added_relevant_predicate;
			
			do
			{
				
				added_relevant_predicate = true;
				
				auto hc_it = horn_constraint_pointers.begin();
				
				while (hc_it != horn_constraint_pointers.end())
				{
					
				
					//
					// Check whether left-hand-side of Horn constraint is satisfied
					//
					bool lhs_satisfied = true;
					for (const auto & dp : (*hc_it)->_premises)
					{
						
						assert (dp->_categorical_data[0] >= 0 && dp->_categorical_data[0] < R.size());
						
						if (!satisfies(*dp, R[dp->_categorical_data[0]]))
						{
							lhs_satisfied = false;
							break;
						}
						
					}
				

					//
					// Left-hand-side of Horn constraint is not satisfied, thus remove it from list (we don't need to look at it again) and proceed to the next
					//
					if (!lhs_satisfied)
					{
						hc_it = horn_constraint_pointers.erase(hc_it);
					}
				
					//
					// Horn constraint is satisfied, proceed but it might be necessary to look at it again
					//
					else if ((*hc_it)->_conclusion != nullptr && satisfies(*((*hc_it)->_conclusion), R[(*hc_it)->_conclusion->_categorical_data[0]]))
					{
						++hc_it;
					}
				
					//
					// If Horn constraint is not consistent with the conjunction, add ALL 0-entries of ALL left-hand-side data points to the conjunction
					// Also remove it from the list as we no longer need to consider it again
					//
					else
					{
						
						// Add relevant predicates
						for (const auto & dp : (*hc_it)->_premises)
						{
							
							auto id = dp->_categorical_data[0];
							auto it_XmR = X_minus_R[id].begin();
						
							while (it_XmR != X_minus_R[id].end())
							{

								if (dp->_int_data[*it_XmR] == 0)
								{
									R[id].insert(*it_XmR);
									it_XmR = X_minus_R[id].erase(it_XmR);
								}
								else
								{
									++it_XmR;
								}
								
							}
							
						}
						
						// Remove Horn constraint from list and proceed to next
						hc_it = horn_constraint_pointers.erase(hc_it);
						added_relevant_predicate = false;
						
					}
				
				}
				
			} while (!added_relevant_predicate);
			
			
			//
			// Debug check
			//
			assert (is_consistent(R, datapoints, horn_constraints));
			
		}
		
		
		static void reduce_predicates_first(const std::vector<datapoint<bool>> & datapoints, const std::vector<horn_constraint<bool>> & horn_constraints, const std::vector<std::set<unsigned>> & X, std::vector<std::set<unsigned>> & R)
		{
			
			//
			// Check arguments
			//
			if (X.empty())
			{
				throw std::runtime_error("X must not be empty");
			}
			else if (X.size() != R.size())
			{
				throw std::runtime_error("R and X must be of same size");
			}
			
			
			//
			// R = R \cap X
			// X_minus_R = X \ R
			//
			std::vector<std::set<unsigned>> X_minus_R(X.size());
			prepare_sets(X, R, X_minus_R);
			

			//
			// Process negative examples
			//
			for (const auto & dp : datapoints)
			{

				auto id = dp._categorical_data[0];
				assert (id >= 0 && id < R.size());
		
				// If negative example satisfies the current conjunction, add the first 0-entry to the conjunction
				if (dp._is_classified && !dp._classification && satisfies(dp, R[id]))
				{
					
					auto size_before = R[id].size();
					auto it_XmR = X_minus_R[id].begin();
					
					while (it_XmR != X_minus_R[id].end())
					{

						if (dp._int_data[*it_XmR] == 0)
						{
							R[id].insert(*it_XmR);
							it_XmR = X_minus_R[id].erase(it_XmR);
							break;
						}
						else
						{
							++it_XmR;
						}
						
					}
					
					assert (size_before < R[id].size());
					
				}
		
			}
			
			
			//
			// Process Horn constraints
			//
			
			// Create observer pointers to Horn constraints
			std::list<const horn_constraint<bool> *> horn_constraint_pointers;
			for (const auto & hc : horn_constraints)
			{
				horn_constraint_pointers.push_back(&hc);
			}
			
			// Do fixed-point computation until all Horn constraints satisfy R
			bool added_relevant_predicate;
			
			do
			{
				
				added_relevant_predicate = true;
				
				auto hc_it = horn_constraint_pointers.begin();
				
				while (hc_it != horn_constraint_pointers.end())
				{
			
					//
					// Check whether left-hand-side of Horn constraint is satisfied
					//
					bool lhs_satisfied = true;
					for (const auto & dp : (*hc_it)->_premises)
					{
						
						assert (dp->_categorical_data[0] >= 0 && dp->_categorical_data[0] < R.size());
						
						if (!satisfies(*dp, R[dp->_categorical_data[0]]))
						{
							lhs_satisfied = false;
							break;
						}
						
					}

					
					//
					// Left-hand-side of Horn constraint is not satisfied, thus remove it from list (we don't need to look at it again) and proceed to the next
					//
					if (!lhs_satisfied)
					{
						hc_it = horn_constraint_pointers.erase(hc_it);
					}
				
					//
					// Horn constraint is satisfied
					//
					else if ((*hc_it)->_conclusion != nullptr && satisfies(*((*hc_it)->_conclusion), R[(*hc_it)->_conclusion->_categorical_data[0]]))
					{
						++hc_it;
					}
				
					//
					// If Horn constraint is not consistent with the conjunction, add ALL 0-entries of ALL left-hand-side data points to the conjunction
					// Also remove it from the list as we no longer need to consider it again
					//
					else
					{
			
						bool added = false;
						
						for (const auto & dp : (*hc_it)->_premises)
						{
							
							auto id = dp->_categorical_data[0];
							auto it_XmR = X_minus_R[id].begin();
						
							while (it_XmR != X_minus_R[id].end() && !added)
							{

								if (dp->_int_data[*it_XmR] == 0)
								{
									R[id].insert(*it_XmR);
									it_XmR = X_minus_R[id].erase(it_XmR);
									added = true;
								}
								else
								{
									++it_XmR;
								}
								
							}
							
							if (added)
							{
								break;
							}
						
						}
						
						assert (added);
						
						// Remove Horn constraint from list and proceed to next
						hc_it = horn_constraint_pointers.erase(hc_it);
						added_relevant_predicate = false;
			
					}
			
				}
				
			} while (!added_relevant_predicate);

			
			//
			// Debug check
			//
			assert (is_consistent(R, datapoints, horn_constraints));
			
		}
		
		
		static void reduce_predicates_greedy(const std::vector<datapoint<bool>> & datapoints, const std::vector<horn_constraint<bool>> & horn_constraints, const std::vector<std::set<unsigned>> & X, std::vector<std::set<unsigned>> & R)
		{
			
			//
			// Check arguments
			//
			if (X.empty())
			{
				throw std::runtime_error("X must not be empty");
			}
			else if (X.size() != R.size())
			{
				throw std::runtime_error("R and X must be of same size");
			}
			
			
			//
			// R = R \cap X
			// X_minus_R = X \ R
			//
			std::vector<std::set<unsigned>> X_minus_R(X.size());
			prepare_sets(X, R, X_minus_R);
			
			
			//
			// Data structures
			//
			
			// Store relevant predicates to chose from
			std::vector<std::map<unsigned, std::pair<std::set<const datapoint<bool> *>, std::set<const horn_constraint<bool> *>>>> predicates(X.size());
			
			
			
			//
			// Process all misclassified negative data points
			//
			for (const auto & dp : datapoints)
			{

				auto id = dp._categorical_data[0];
				assert (id >= 0 && id < R.size());
		
				// If negative example satisfies the current conjunction, store relevant predicates
				if (dp._is_classified && !dp._classification && satisfies(dp, R[id]))
				{
					
					for (const auto p : X_minus_R[id])
					{
						if (dp._int_data[p] == 0)
						{
							predicates[id][p].first.insert(&dp);
						}
					}
				
				}
		
			}


			//
			// Add relevant predicates in a fixed-point manner until all  Horn constraints are satisfied.
			// Note that negative unsatisfied data points will be satisfied after the first iteration.
			//
			
			// Create observer pointers to Horn constraints
			std::list<const horn_constraint<bool> *> horn_constraint_pointers;
			for (const auto & hc : horn_constraints)
			{
				horn_constraint_pointers.push_back(&hc);
			}
			
			// Start fixed-point computation
			bool done;
			do
			{
				
				std::vector<std::pair<unsigned, unsigned>> new_relevant_predicates;
				done = true;
				
				//
				// Find relevant predicates for Horn constraints that are not satisfied
				//
				for (const auto & hc : horn_constraint_pointers)
				{
					
					
					//
					// Check whether left-hand-side of Horn constraint is satisfied
					//
					bool lhs_satisfied = true;
					for (const auto & dp : hc->_premises)
					{
						
						assert (dp->_categorical_data[0] >= 0 && dp->_categorical_data[0] < R.size());
						
						if (!satisfies(*dp, R[dp->_categorical_data[0]]))
						{
							lhs_satisfied = false;
							break;
						}
						
					}
					
					// If Horn constraint does not satisfy the current conjunction, store relevant predicates for further processing
					if (lhs_satisfied && (hc->_conclusion == nullptr || !satisfies(*(hc->_conclusion), R[hc->_conclusion->_categorical_data[0]])))
					{

						done = false;
				
						for (const auto & dp : hc->_premises)
						{
							
							auto id = dp->_categorical_data[0];
							
							for (const auto p : X_minus_R[id])
							{
								if (dp->_int_data[p] == 0)
								{
									predicates[id][p].second.insert(hc);
								}
							}
							
						}
						
					}
					
				}
				
				
				//
				// Greedily find best relevant predicates and add them
				//
				
				bool found_unprocessed_predicate;
				
				do
				{
					
					found_unprocessed_predicate = false;
					
					//
					// Find relevant predicate that fixes most negative data points and Horn constraints
					//
					unsigned best_value = 0;
					unsigned best_id = 0;
					unsigned best_p = 0;
					
					for (unsigned i=0; i<predicates.size(); ++i)
					{
						for (const auto & pair : predicates[i])
						{
							if (pair.second.first.size() + pair.second.second.size() > best_value)
							{
								best_value = pair.second.first.size() + pair.second.second.size();
								best_id = i;
								best_p = pair.first;
								found_unprocessed_predicate = true;
							}
						}
						
					}
					
					
					//
					// Add best relevant predicate and adapt information of other relevant predicates accordingly
					//
					if (found_unprocessed_predicate)
					{
						
						// Negative data points
						for (const auto & dp : predicates.at(best_id).at(best_p).first)
						{
							for (const auto p : X_minus_R[dp->_categorical_data[0]])
							{
								if (dp->_int_data[p] == 0)
								{
									predicates.at(dp->_categorical_data[0]).at(p).first.erase(dp);
								}
							}
						}
						
						// Horn constraints
						for (const auto & hc : predicates.at(best_id).at(best_p).second)
						{
							for (const auto & dp : hc->_premises)
							{
								for (const auto p : X_minus_R[dp->_categorical_data[0]])
								{
									if (dp->_int_data[p] == 0)
									{
										predicates.at(dp->_categorical_data[0]).at(p).second.erase(hc);
									}
								}
							}
						}
						
						// Add relevant predicate
						new_relevant_predicates.push_back(std::pair<unsigned, unsigned>(best_id, best_p));
						predicates.at(best_id).erase(best_p);
						done = false;
						
					}
					
				} while (found_unprocessed_predicate);

				// DEBUG check
				for (unsigned i=0; i<predicates.size(); ++i)
				{
					for (const auto & pair : predicates[i])
					{
						std::cout << i << "," << pair.first << std::endl;
						assert (pair.second.first.empty() && pair.second.second.empty());
					}
				}

				// Add relevant predicates and update sets accordingly
				for (const auto & pair : new_relevant_predicates)
				{
					R[pair.first].insert(pair.second);
					X_minus_R[pair.first].erase(pair.second);
				}
			
				
			} while (!done);

			
			//
			// Debug check
			//
			assert (is_consistent(R, datapoints, horn_constraints));
			
		}
		
		
		static void reduce_predicates_minimal(const std::vector<datapoint<bool>> & datapoints, const std::vector<horn_constraint<bool>> & horn_constraints, const std::vector<std::set<unsigned>> & X, std::vector<std::set<unsigned>> & R)
		{
			
			//
			// Check arguments
			//
			if (X.empty())
			{
				throw std::runtime_error("X must not be empty");
			}
			else if (X.size() != R.size())
			{
				throw std::runtime_error("R and X must be of same size");
			}
			
			
			//
			// R = R \cap X
			// X_minus_R = X \ R
			//
			std::vector<std::set<unsigned>> X_minus_R(X.size());
			prepare_sets(X, R, X_minus_R);
			
			
			//
			// Prepare data structure to store variables
			//
			z3::context ctx;
			unsigned var_count = 0;
			std::vector<std::map<unsigned, z3::expr>> attr2expr(X.size());
			for (unsigned i=0; i<X.size(); ++i)
			{
				for (const auto p : X_minus_R[i])
				{
					attr2expr[i].insert(attr2expr[i].end(), std::pair<unsigned, z3::expr>(p, ctx.constant(ctx.int_symbol(var_count++), ctx.bool_sort())));
				}
			}

			
			//
			// Create variables and expression for negative data points
			//
			z3::expr neg_expr = ctx.bool_val(true);
			for (const auto & dp : datapoints)
			{
		
				auto id = dp._categorical_data[0];
		
				// If negative example satisfies the current conjunction, create clause
				if (dp._is_classified && !dp._classification && satisfies(dp, R[id]))
				{
					
					std::cout << "Adding negative" << std::endl;
					
					z3::expr clause = ctx.bool_val(false);
					
					for (const auto p : X_minus_R[id])
					{
						if (dp._int_data[p] == 0)
						{
							clause = clause || attr2expr[id].at(p);
						}
					}
					
					neg_expr = neg_expr && clause;
					
				}
			
			}
			
			
			//
			// Create variables and expression for Horn constraints
			//
			z3::expr horn_expr = ctx.bool_val(true);
			for (const auto & hc : horn_constraints)
			{
				
				//
				// Check whether left-hand-side of Horn constraint is satisfied
				//
				bool lhs_satisfied = true;
				for (const auto & dp : hc._premises)
				{
					
					const auto id = dp->_categorical_data[0];
					assert (id >= 0 && id < R.size());
					
					if (!satisfies(*dp, R[id]))
					{
						lhs_satisfied = false;
						break;
					}
					
				}

				
				//
				// If Horn constraint is not consistent with the conjunction, create clause
				//
				if (lhs_satisfied && (hc._conclusion == nullptr || !satisfies(*hc._conclusion, R[hc._conclusion->_categorical_data[0]])))
				{
			
					// Create clause
					z3::expr clause = ctx.bool_val(false);
			
					for (const auto & dp : hc._premises)
					{
						
						std::cout << *dp << std::endl;
						
						const auto id = dp->_categorical_data[0];
						
						for (const auto pair : attr2expr[id])
						{
							if (dp->_int_data[pair.first] == 0)
							{
								clause = clause || pair.second;
							}
						}
						
					}
					
					horn_expr = horn_expr && clause;
				
				}
				
				//
				// If Horn constraint is consistent, create expression that prevents that right-hand-side gets unsatisfied
				//
				if (lhs_satisfied && hc._conclusion != nullptr && satisfies(*hc._conclusion, R[hc._conclusion->_categorical_data[0]]))
				{
				
					z3::expr clause = ctx.bool_val(false);
					
					for (const auto & dp : hc._premises)
					{
						
						const auto id = dp->_categorical_data[0];
						
						for (const auto pair : attr2expr[id])
						{
							if (dp->_int_data[pair.first] == 0)
							{
								clause = clause || pair.second;
							}
						}
						
					}
					
					
					z3::expr expr = ctx.bool_val(true);

					const auto id = hc._conclusion->_categorical_data[0];
				
					for (const auto pair : attr2expr[id])
					{
						if (hc._conclusion->_int_data[pair.first] == 0)
						{
							expr = expr && !pair.second;
						}
					}
				
					horn_expr = horn_expr && (clause || expr);
				
				}
				
			}

			
			//
			// Solve
			//
			unsigned k = 1;
			bool done = false;
			
			do
			{
				
				// Create size constraint
				z3::expr size_constraint = ctx.int_val(0);
				for (unsigned i=0; i<X.size(); ++i)
				{
					for (const auto pair : attr2expr[i])
					{
						size_constraint = size_constraint + (z3::ite(pair.second, ctx.int_val(1), ctx.int_val(0)));
					}
				}
				size_constraint = size_constraint <= ctx.int_val(k);
				
				z3::solver solver(ctx);
				solver.add(neg_expr);
				solver.add(horn_expr);
				solver.add(size_constraint);
				
			
				// Satisfiable
				if (solver.check() == z3::check_result::sat)
				{
					
					auto model = solver.get_model();
					
					// Add predicates
					for (unsigned i=0; i<attr2expr.size(); ++i)
					{
						for (const auto pair : attr2expr[i])
						{
							
							if (model.eval(pair.second).bool_value() == Z3_L_TRUE)
							{
								R[i].insert(pair.first);
							}
						}
					}
			
					done = true;
					
				}
				else
				{
					
					if (k >= var_count)
					{
						throw std::runtime_error("k >= var_count");
					}

					++k;

				}
			
				
			} while (!done);
			
			
			//
			// Debug check
			//
			assert (is_consistent(R, datapoints, horn_constraints));
			
		}
		
		
		//
		// Computes R = R \cap X and X_minus_R = X \ R in place
		//
		static void prepare_sets(const std::vector<std::set<unsigned>> & X, std::vector<std::set<unsigned>> & R, std::vector<std::set<unsigned>> & X_minus_R)
		{
			
			//
			// R = R \cap X
			// X_minus_R = X \ R
			//

			for (unsigned i = 0; i < X.size(); ++i)
			{

				auto R_it = R[i].begin();
				auto X_it = X[i].cbegin();
				
				while (R_it != R[i].end() && X_it != X[i].cend())
				{
					
					// In R but not in X (remove from R)
					if (*R_it < *X_it)
					{
						R_it = R[i].erase(R_it);
					}
					
					// In X but not in R (add to X\R)
					else if (*X_it < *R_it)
					{
						X_minus_R[i].insert(X_minus_R[i].end(), *X_it);
						++X_it;
					}
					
					// In both X and R (skip)
					else 
					{
						++R_it;
						++X_it;
					}
					
				}
				
				R[i].erase(R_it, R[i].end());
				X_minus_R[i].insert(X_it, X[i].cend());
			
			}
			
		}

		
		static bool is_consistent(const std::vector<std::set<unsigned>> & predicates, const std::vector<datapoint<bool>> & datapoints, const std::vector<horn_constraint<bool>> & horn_constraints, bool vervose=false)
		{
			//
			// Check positive and negative data points
			//
			for (const auto & dp : datapoints)
			{
				if (dp._is_classified)
				{
				
					auto classification = satisfies(dp, predicates[dp._categorical_data[0]]);

					if (classification && !dp._classification)
					{
						std::cerr << dp << " not consistent!" << std::endl;
						return false;
					}
					else if (!classification && dp._classification)
					{
						std::cerr << dp << " not consistent!" << std::endl;
						return false;
					}
					
				}
			
			}
			
			
			//
			// Check Horn constraints
			//
			for (const auto & hc : horn_constraints)
			{
				
				// Check lhs
				bool satisfies_lhs = true;
				for (const auto & premis : hc._premises)
				{
					if (!satisfies(*premis, predicates[premis->_categorical_data[0]]))
					{
						satisfies_lhs = false;
						break;
					}
				}
				
				// Check rhs if lhs is atisfied
				if (satisfies_lhs && (hc._conclusion == nullptr || !satisfies(*hc._conclusion, predicates[hc._conclusion->_categorical_data[0]])))
				{
					for (unsigned i=0; i<predicates.size(); ++i)
					{
						std::cerr << "[ ";
						for (const auto p : predicates[i])
						{
							std::cerr << p << " ";
						}
						std::cerr << "]" << std::endl;
					}
					std::cerr << hc << " not consistent!" << std::endl;
					return false;
				}
				
			}
			
			
			return true;
			
		}
		

		static void write_R_file (const std::string & filename, const std::vector<std::set<unsigned>> & R)
		{
			
			//
			// Open file
			//
			std::ofstream outfile(filename);
			// Check opening the file failed
			if (outfile.fail())
			{
				throw std::runtime_error("Error opening " + filename);
			}
	
			
			//
			// Write
			//
			for (unsigned i = 0; i < R.size(); ++i)
			{
				
				if (i > 0)
				{
					outfile << std::endl;
				}
				
				// Empty conjunction
				if (R[i].empty())
				{
					outfile << "e";
				}
				
				// Non-empty conjunction
				else
				{
					for (const auto & p : R[i])
					{
						outfile << p << " ";
					}
				}
				
			}
	
			
			//
			// Close file
			//
			outfile.close();
	
		}
	
	
		
		static std::vector<std::set<unsigned>> read_R_file (const std::string & filename)
		{
			
			//
			// Open file
			//
			std::ifstream infile(filename);
			// Check opening the file failed
			if (infile.fail())
			{
				throw std::runtime_error("Error opening " + filename);
			}
			
			
			std::vector<std::set<unsigned>> R;
			
			
			//
			// Read file line by line
			//
			std::string line;
			while (std::getline(infile, line))
			{
				
				// Skip empty lines
				if (line.empty())
				{
					continue;
				}
				
				// Empty conjunction
				else if (line == "e")
				{
					R.push_back(std::set<unsigned>());
				}
				
				// Non-empty conjunction
				else
				{
					std::set<unsigned> cur_R;
					std::stringstream line_stream(line);
					unsigned p;
				
					// Read predicates from line
					while (line_stream >> p)
					{
						cur_R.insert(p);
					}
				
					R.push_back(std::move(cur_R)); // No use of cur_R beyond this point
					
				}
			
			}
			
			
			//
			// Close file
			//
			infile.close();
			
			return R;
			
		}
		
	};

}; // End namespace horn_verification

#endif