// C++ includes
#include <iostream>
#include <string>

// C includes
#include <cassert>
#include <unistd.h>

// Project includes
#include "boogie_io.h"
#include "sorcar.h"


/**
 * Displays usage information.
 *
 * @param out The output stream to write to
 */
void display_usage (std::ostream & out)
{
	
	out << "Usage: sorcar [options] file_stem" << std::endl;
	out << "Options:" << std::endl;
	out << "  -a <algorithm>\tSelects the learning algorithm. Valid options are:" << std::endl;
	out << "\t\t\thorndini, sorcar, sorcar-first, sorcar-greedy" << std::endl;
	out << "\t\t\tsorcar-minimal" << std::endl;
	out << "  -f\t\t\tRuns Horndini in the first round." << std::endl;
	out << "  -t\t\t\tAlternates Horndini and Sorcar between rounds." << std::endl;
	out << "  -r\t\t\tResets the set R in each round." << std::endl;

}


/**
 * Enum for selecting the algorithm chosen by the user.
 */
enum algorithm
{
	
	/// Horndini
	HORNDINI = 0,
	
	/// Sorcar with adding all relevant predicates
	SORCAR,
	
	/// Sorcar with greedily adding the first relevant predicate
	SORCAR_FIRST,
	
	/// Sorcar with selecting relevant predicates set using a greedy hitting set algorithm
	SORCAR_GREEDY,
	
	/// Sorcar with selecting a minimal set of relevant predicates
	SORCAR_MINIMAL,
	
};

using namespace horn_verification;
 

 /**
  * Command line interface to Sorcar.
  *
  * @param argc The number of command line arguments
  * @param argv The command line arguments
  *
  * @return Returns an POSIX compliant exit code.
  */
int main(int argc, char * const argv[]) {
	
	//
	// Parse command line options
	//
	
	// Variables to hold command line arguments
	bool reset_R = false;
	bool horndini_first_round = false;
	bool alternate = false;
	algorithm alg = algorithm::SORCAR;
	
	// Parse
	int opt;
	while ((opt = getopt(argc, argv, "a:frt")) != -1)
	{
		
		switch(opt)
		{
			
			// Algorithm selection
			case 'a':
			{
				
				std::string arg(optarg);
				
				// Parse algorithm
				if (arg == "horndini")
				{
					alg = algorithm::HORNDINI;
				}
				else if (arg == "sorcar")
				{
					alg = algorithm::SORCAR;
				}
				else if (arg == "sorcar-first")
				{
					alg = algorithm::SORCAR_FIRST;
				}
				else if (arg == "sorcar-greedy")
				{
					alg = algorithm::SORCAR_GREEDY;
				}
				else if (arg == "sorcar-minimal")
				{
					alg = algorithm::SORCAR_MINIMAL;
				}
				else
				{
					std::cout << "Unknown algorithm '" << arg << "'" << std::endl;
					display_usage(std::cout);
					return EXIT_FAILURE;
				}
				
				break;
				
			}
			
			// Horndini in first round
			case 'f':
			{
				horndini_first_round = true;
				break;
			}

			// Reset R
			case 'r':
			{
				reset_R = true;
				break;
			}
			
			// Alternate between Horndini and Sorcar
			case 't':
			{
				alternate = true;
				break;
			}
			
			// Error (should not happen)
			default:
			{
				display_usage(std::cout);
				return EXIT_FAILURE;
			}
			
		}
		
	}
	
	// Check if only file stem is given
	if (optind != argc - 1)
	{
		std::cout << "Invalid command line arguments." << std::endl;
		display_usage(std::cout);
		return EXIT_FAILURE;
	}

	// File stem
	auto file_stem = std::string(argv[argc - 1]);


	//
	// Read input from files
	//
	
	// Read attribute meta data
	auto metadata = boogie_io::read_attributes_file(file_stem + ".attributes");
	
	// Read data points
	auto datapoints = boogie_io::read_data_file(file_stem + ".data", metadata);

	// Read horn constraints
	auto horn_constraints = boogie_io::read_horn_file(file_stem + ".horn", datapoints);

	// Read intervals
	auto intervals = boogie_io::read_intervals_file(file_stem + ".intervals");

	// Read status (currently only the round number)
	auto round = boogie_io::read_status_file(file_stem + ".status");

	
	//
	// Check input
	//
	if (metadata.int_names().size() + metadata.categorical_names().size() == 0)
	{
		throw std::runtime_error("No attributes defined");
	}

	//
	// Horndini, compute X
	//
	auto X = sorcar::horndini(datapoints, horn_constraints, intervals);
	assert (sorcar::is_consistent(X, datapoints, horn_constraints, true));

	
	std::ofstream out("log.txt", std::ios_base::app);
	out << "alg=" << alg << "; alternate=" << alternate << "; reset-R=" << reset_R << "; first round=" << horndini_first_round;
	
	
	//
	// Plain Horndini (just output Horndini results)
	//
	if (alg == algorithm::HORNDINI)
	{
		boogie_io::write_JSON_file(metadata, X, file_stem + ".json");
		//sorcar::write_R_file(file_stem + ".R", X);
		out << "; running Horndini; NOT writing R file" << std::endl;
	}
	
	//
	// Sorcar
	//
	else if (alg == algorithm::SORCAR || alg == algorithm::SORCAR_FIRST || alg == algorithm::SORCAR_GREEDY || alg == algorithm::SORCAR_MINIMAL)
	{

		// Prepare set R
		std::vector<std::set<unsigned>> R;
		if (reset_R || round == 1)
		{
			R.resize(X.size());
		}
		else
		{
			R = sorcar::read_R_file(file_stem + ".R");
		}
		out << "; empty R: " << (reset_R || round == 1);
		
		// Run Sorcar if desired and output results
		if (!(horndini_first_round && round == 1) && !(alternate && round % 2 == 1))
		{
			
			// Run Sorcar adding first relevant predicate
			if (alg == algorithm::SORCAR_FIRST)
			{
				sorcar::reduce_predicates_first(datapoints, horn_constraints, X, R);
				out << "; running first Sorcar";
			}

			// Run Sorcar using greedy algorithm for hitting set
			else if (alg == algorithm::SORCAR_GREEDY)
			{
				sorcar::reduce_predicates_greedy(datapoints, horn_constraints, X, R);
				out << "; running greedy Sorcar";
			}

			// Run Sorcar using minimal selection of relevant predicates
			else if (alg == algorithm::SORCAR_MINIMAL)
			{
				sorcar::reduce_predicates_minimal(datapoints, horn_constraints, X, R);
				out << "; running minimal Sorcar";
			}
			
			// Run vanilla Sorcar (adding all relevant predicates)
			else
			{
				sorcar::reduce_predicates_all(datapoints, horn_constraints, X, R);
				out << "; running vanilla Sorcar";
			}
			
			
			// Output
			boogie_io::write_JSON_file(metadata, R, file_stem + ".json");
			sorcar::write_R_file(file_stem + ".R", R);
			out << "; writing R file" << std::endl;
			
		}
		
		// Otherwise, just output Horndini results
		else
		{
			boogie_io::write_JSON_file(metadata, X, file_stem + ".json");
			sorcar::write_R_file(file_stem + ".R", X);
			out << "; writing R file" << std::endl;
		}
		
	}

	// Error
	else
	{
		throw std::runtime_error("Unknown algorithm selected");
	}
	
	out.close();
	
}
