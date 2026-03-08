/* A Bison parser, made by GNU Bison 3.0.4.  */

/* Bison implementation for Yacc-like parsers in C

   Copyright (C) 1984, 1989-1990, 2000-2015 Free Software Foundation, Inc.

   This program is free software: you can redistribute it and/or modify
   it under the terms of the GNU General Public License as published by
   the Free Software Foundation, either version 3 of the License, or
   (at your option) any later version.

   This program is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
   GNU General Public License for more details.

   You should have received a copy of the GNU General Public License
   along with this program.  If not, see <http://www.gnu.org/licenses/>.  */

/* As a special exception, you may create a larger work that contains
   part or all of the Bison parser skeleton and distribute that work
   under terms of your choice, so long as that work isn't itself a
   parser generator using the skeleton or a modified version thereof
   as a parser skeleton.  Alternatively, if you modify or redistribute
   the parser skeleton itself, you may (at your option) remove this
   special exception, which will cause the skeleton and the resulting
   Bison output files to be licensed under the GNU General Public
   License without this special exception.

   This special exception was added by the Free Software Foundation in
   version 2.2 of Bison.  */

/* C LALR(1) parser skeleton written by Richard Stallman, by
   simplifying the original so-called "semantic" parser.  */

/* All symbols defined below should begin with yy or YY, to avoid
   infringing on user name space.  This should be done even for local
   variables, as they might otherwise be expanded by user macros.
   There are some unavoidable exceptions within include files to
   define necessary library symbols; they are noted "INFRINGES ON
   USER NAME SPACE" below.  */

/* Identify Bison output.  */
#define YYBISON 1

/* Bison version.  */
#define YYBISON_VERSION "3.0.4"

/* Skeleton name.  */
#define YYSKELETON_NAME "yacc.c"

/* Pure parsers.  */
#define YYPURE 0

/* Push parsers.  */
#define YYPUSH 0

/* Pull parsers.  */
#define YYPULL 1


/* Substitute the variable and function names.  */
#define yyparse         dvdvmparse
#define yylex           dvdvmlex
#define yyerror         dvdvmerror
#define yydebug         dvdvmdebug
#define yynerrs         dvdvmnerrs

#define yylval          dvdvmlval
#define yychar          dvdvmchar

/* Copy the first part of user declarations.  */
#line 1 "dvdvmy.y" /* yacc.c:339  */


/*
 * Copyright (C) 2002 Scott Smith (trckjunky@users.sourceforge.net)
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or (at
 * your option) any later version.
 *
 * This program is distributed in the hope that it will be useful, but
 * WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston,
 * MA 02110-1301 USA.
 */

#include "config.h"
#include "compat.h" /* needed for bool */
#include "dvdvm.h"


#define YYERROR_VERBOSE


#line 104 "dvdvmy.c" /* yacc.c:339  */

# ifndef YY_NULLPTR
#  if defined __cplusplus && 201103L <= __cplusplus
#   define YY_NULLPTR nullptr
#  else
#   define YY_NULLPTR 0
#  endif
# endif

/* Enabling verbose error messages.  */
#ifdef YYERROR_VERBOSE
# undef YYERROR_VERBOSE
# define YYERROR_VERBOSE 1
#else
# define YYERROR_VERBOSE 0
#endif

/* In a future release of Bison, this section will be replaced
   by #include "dvdvmy.h".  */
#ifndef YY_DVDVM_DVDVMY_H_INCLUDED
# define YY_DVDVM_DVDVMY_H_INCLUDED
/* Debug traces.  */
#ifndef YYDEBUG
# define YYDEBUG 0
#endif
#if YYDEBUG
extern int dvdvmdebug;
#endif

/* Token type.  */
#ifndef YYTOKENTYPE
# define YYTOKENTYPE
  enum yytokentype
  {
    NUM_TOK = 258,
    G_TOK = 259,
    S_TOK = 260,
    ID_TOK = 261,
    ANGLE_TOK = 262,
    AUDIO_TOK = 263,
    BREAK_TOK = 264,
    BUTTON_TOK = 265,
    CALL_TOK = 266,
    CELL_TOK = 267,
    CHAPTER_TOK = 268,
    CLOSEBRACE_TOK = 269,
    CLOSEPAREN_TOK = 270,
    COUNTER_TOK = 271,
    ELSE_TOK = 272,
    ENTRY_TOK = 273,
    EXIT_TOK = 274,
    FPC_TOK = 275,
    GOTO_TOK = 276,
    IF_TOK = 277,
    JUMP_TOK = 278,
    MENU_TOK = 279,
    NEXT_TOK = 280,
    OPENBRACE_TOK = 281,
    OPENPAREN_TOK = 282,
    PGC_TOK = 283,
    PREV_TOK = 284,
    PROGRAM_TOK = 285,
    PTT_TOK = 286,
    REGION_TOK = 287,
    RESUME_TOK = 288,
    RND_TOK = 289,
    ROOT_TOK = 290,
    SET_TOK = 291,
    SUBTITLE_TOK = 292,
    TAIL_TOK = 293,
    TITLE_TOK = 294,
    TITLESET_TOK = 295,
    TOP_TOK = 296,
    UP_TOK = 297,
    VMGM_TOK = 298,
    _OR_TOK = 299,
    XOR_TOK = 300,
    LOR_TOK = 301,
    BOR_TOK = 302,
    _AND_TOK = 303,
    LAND_TOK = 304,
    BAND_TOK = 305,
    NOT_TOK = 306,
    EQ_TOK = 307,
    NE_TOK = 308,
    GE_TOK = 309,
    GT_TOK = 310,
    LE_TOK = 311,
    LT_TOK = 312,
    ADD_TOK = 313,
    SUB_TOK = 314,
    MUL_TOK = 315,
    DIV_TOK = 316,
    MOD_TOK = 317,
    ADDSET_TOK = 318,
    SUBSET_TOK = 319,
    MULSET_TOK = 320,
    DIVSET_TOK = 321,
    MODSET_TOK = 322,
    ANDSET_TOK = 323,
    ORSET_TOK = 324,
    XORSET_TOK = 325,
    SEMICOLON_TOK = 326,
    COLON_TOK = 327,
    ERROR_TOK = 328
  };
#endif

/* Value type.  */
#if ! defined YYSTYPE && ! defined YYSTYPE_IS_DECLARED

union YYSTYPE
{
#line 92 "dvdvmy.y" /* yacc.c:355  */

    unsigned int int_val;
    char *str_val;
    struct vm_statement *statement;

#line 224 "dvdvmy.c" /* yacc.c:355  */
};

typedef union YYSTYPE YYSTYPE;
# define YYSTYPE_IS_TRIVIAL 1
# define YYSTYPE_IS_DECLARED 1
#endif


extern YYSTYPE dvdvmlval;

int dvdvmparse (void);

#endif /* !YY_DVDVM_DVDVMY_H_INCLUDED  */

/* Copy the second part of user declarations.  */

#line 241 "dvdvmy.c" /* yacc.c:358  */

#ifdef short
# undef short
#endif

#ifdef YYTYPE_UINT8
typedef YYTYPE_UINT8 yytype_uint8;
#else
typedef unsigned char yytype_uint8;
#endif

#ifdef YYTYPE_INT8
typedef YYTYPE_INT8 yytype_int8;
#else
typedef signed char yytype_int8;
#endif

#ifdef YYTYPE_UINT16
typedef YYTYPE_UINT16 yytype_uint16;
#else
typedef unsigned short int yytype_uint16;
#endif

#ifdef YYTYPE_INT16
typedef YYTYPE_INT16 yytype_int16;
#else
typedef short int yytype_int16;
#endif

#ifndef YYSIZE_T
# ifdef __SIZE_TYPE__
#  define YYSIZE_T __SIZE_TYPE__
# elif defined size_t
#  define YYSIZE_T size_t
# elif ! defined YYSIZE_T
#  include <stddef.h> /* INFRINGES ON USER NAME SPACE */
#  define YYSIZE_T size_t
# else
#  define YYSIZE_T unsigned int
# endif
#endif

#define YYSIZE_MAXIMUM ((YYSIZE_T) -1)

#ifndef YY_
# if defined YYENABLE_NLS && YYENABLE_NLS
#  if ENABLE_NLS
#   include <libintl.h> /* INFRINGES ON USER NAME SPACE */
#   define YY_(Msgid) dgettext ("bison-runtime", Msgid)
#  endif
# endif
# ifndef YY_
#  define YY_(Msgid) Msgid
# endif
#endif

#ifndef YY_ATTRIBUTE
# if (defined __GNUC__                                               \
      && (2 < __GNUC__ || (__GNUC__ == 2 && 96 <= __GNUC_MINOR__)))  \
     || defined __SUNPRO_C && 0x5110 <= __SUNPRO_C
#  define YY_ATTRIBUTE(Spec) __attribute__(Spec)
# else
#  define YY_ATTRIBUTE(Spec) /* empty */
# endif
#endif

#ifndef YY_ATTRIBUTE_PURE
# define YY_ATTRIBUTE_PURE   YY_ATTRIBUTE ((__pure__))
#endif

#ifndef YY_ATTRIBUTE_UNUSED
# define YY_ATTRIBUTE_UNUSED YY_ATTRIBUTE ((__unused__))
#endif

#if !defined _Noreturn \
     && (!defined __STDC_VERSION__ || __STDC_VERSION__ < 201112)
# if defined _MSC_VER && 1200 <= _MSC_VER
#  define _Noreturn __declspec (noreturn)
# else
#  define _Noreturn YY_ATTRIBUTE ((__noreturn__))
# endif
#endif

/* Suppress unused-variable warnings by "using" E.  */
#if ! defined lint || defined __GNUC__
# define YYUSE(E) ((void) (E))
#else
# define YYUSE(E) /* empty */
#endif

#if defined __GNUC__ && 407 <= __GNUC__ * 100 + __GNUC_MINOR__
/* Suppress an incorrect diagnostic about yylval being uninitialized.  */
# define YY_IGNORE_MAYBE_UNINITIALIZED_BEGIN \
    _Pragma ("GCC diagnostic push") \
    _Pragma ("GCC diagnostic ignored \"-Wuninitialized\"")\
    _Pragma ("GCC diagnostic ignored \"-Wmaybe-uninitialized\"")
# define YY_IGNORE_MAYBE_UNINITIALIZED_END \
    _Pragma ("GCC diagnostic pop")
#else
# define YY_INITIAL_VALUE(Value) Value
#endif
#ifndef YY_IGNORE_MAYBE_UNINITIALIZED_BEGIN
# define YY_IGNORE_MAYBE_UNINITIALIZED_BEGIN
# define YY_IGNORE_MAYBE_UNINITIALIZED_END
#endif
#ifndef YY_INITIAL_VALUE
# define YY_INITIAL_VALUE(Value) /* Nothing. */
#endif


#if ! defined yyoverflow || YYERROR_VERBOSE

/* The parser invokes alloca or malloc; define the necessary symbols.  */

# ifdef YYSTACK_USE_ALLOCA
#  if YYSTACK_USE_ALLOCA
#   ifdef __GNUC__
#    define YYSTACK_ALLOC __builtin_alloca
#   elif defined __BUILTIN_VA_ARG_INCR
#    include <alloca.h> /* INFRINGES ON USER NAME SPACE */
#   elif defined _AIX
#    define YYSTACK_ALLOC __alloca
#   elif defined _MSC_VER
#    include <malloc.h> /* INFRINGES ON USER NAME SPACE */
#    define alloca _alloca
#   else
#    define YYSTACK_ALLOC alloca
#    if ! defined _ALLOCA_H && ! defined EXIT_SUCCESS
#     include <stdlib.h> /* INFRINGES ON USER NAME SPACE */
      /* Use EXIT_SUCCESS as a witness for stdlib.h.  */
#     ifndef EXIT_SUCCESS
#      define EXIT_SUCCESS 0
#     endif
#    endif
#   endif
#  endif
# endif

# ifdef YYSTACK_ALLOC
   /* Pacify GCC's 'empty if-body' warning.  */
#  define YYSTACK_FREE(Ptr) do { /* empty */; } while (0)
#  ifndef YYSTACK_ALLOC_MAXIMUM
    /* The OS might guarantee only one guard page at the bottom of the stack,
       and a page size can be as small as 4096 bytes.  So we cannot safely
       invoke alloca (N) if N exceeds 4096.  Use a slightly smaller number
       to allow for a few compiler-allocated temporary stack slots.  */
#   define YYSTACK_ALLOC_MAXIMUM 4032 /* reasonable circa 2006 */
#  endif
# else
#  define YYSTACK_ALLOC YYMALLOC
#  define YYSTACK_FREE YYFREE
#  ifndef YYSTACK_ALLOC_MAXIMUM
#   define YYSTACK_ALLOC_MAXIMUM YYSIZE_MAXIMUM
#  endif
#  if (defined __cplusplus && ! defined EXIT_SUCCESS \
       && ! ((defined YYMALLOC || defined malloc) \
             && (defined YYFREE || defined free)))
#   include <stdlib.h> /* INFRINGES ON USER NAME SPACE */
#   ifndef EXIT_SUCCESS
#    define EXIT_SUCCESS 0
#   endif
#  endif
#  ifndef YYMALLOC
#   define YYMALLOC malloc
#   if ! defined malloc && ! defined EXIT_SUCCESS
void *malloc (YYSIZE_T); /* INFRINGES ON USER NAME SPACE */
#   endif
#  endif
#  ifndef YYFREE
#   define YYFREE free
#   if ! defined free && ! defined EXIT_SUCCESS
void free (void *); /* INFRINGES ON USER NAME SPACE */
#   endif
#  endif
# endif
#endif /* ! defined yyoverflow || YYERROR_VERBOSE */


#if (! defined yyoverflow \
     && (! defined __cplusplus \
         || (defined YYSTYPE_IS_TRIVIAL && YYSTYPE_IS_TRIVIAL)))

/* A type that is properly aligned for any stack member.  */
union yyalloc
{
  yytype_int16 yyss_alloc;
  YYSTYPE yyvs_alloc;
};

/* The size of the maximum gap between one aligned stack and the next.  */
# define YYSTACK_GAP_MAXIMUM (sizeof (union yyalloc) - 1)

/* The size of an array large to enough to hold all stacks, each with
   N elements.  */
# define YYSTACK_BYTES(N) \
     ((N) * (sizeof (yytype_int16) + sizeof (YYSTYPE)) \
      + YYSTACK_GAP_MAXIMUM)

# define YYCOPY_NEEDED 1

/* Relocate STACK from its old location to the new one.  The
   local variables YYSIZE and YYSTACKSIZE give the old and new number of
   elements in the stack, and YYPTR gives the new location of the
   stack.  Advance YYPTR to a properly aligned location for the next
   stack.  */
# define YYSTACK_RELOCATE(Stack_alloc, Stack)                           \
    do                                                                  \
      {                                                                 \
        YYSIZE_T yynewbytes;                                            \
        YYCOPY (&yyptr->Stack_alloc, Stack, yysize);                    \
        Stack = &yyptr->Stack_alloc;                                    \
        yynewbytes = yystacksize * sizeof (*Stack) + YYSTACK_GAP_MAXIMUM; \
        yyptr += yynewbytes / sizeof (*yyptr);                          \
      }                                                                 \
    while (0)

#endif

#if defined YYCOPY_NEEDED && YYCOPY_NEEDED
/* Copy COUNT objects from SRC to DST.  The source and destination do
   not overlap.  */
# ifndef YYCOPY
#  if defined __GNUC__ && 1 < __GNUC__
#   define YYCOPY(Dst, Src, Count) \
      __builtin_memcpy (Dst, Src, (Count) * sizeof (*(Src)))
#  else
#   define YYCOPY(Dst, Src, Count)              \
      do                                        \
        {                                       \
          YYSIZE_T yyi;                         \
          for (yyi = 0; yyi < (Count); yyi++)   \
            (Dst)[yyi] = (Src)[yyi];            \
        }                                       \
      while (0)
#  endif
# endif
#endif /* !YYCOPY_NEEDED */

/* YYFINAL -- State number of the termination state.  */
#define YYFINAL  46
/* YYLAST -- Last index in YYTABLE.  */
#define YYLAST   433

/* YYNTOKENS -- Number of terminals.  */
#define YYNTOKENS  74
/* YYNNTS -- Number of nonterminals.  */
#define YYNNTS  18
/* YYNRULES -- Number of rules.  */
#define YYNRULES  96
/* YYNSTATES -- Number of states.  */
#define YYNSTATES  191

/* YYTRANSLATE[YYX] -- Symbol number corresponding to YYX as returned
   by yylex, with out-of-bounds checking.  */
#define YYUNDEFTOK  2
#define YYMAXUTOK   328

#define YYTRANSLATE(YYX)                                                \
  ((unsigned int) (YYX) <= YYMAXUTOK ? yytranslate[YYX] : YYUNDEFTOK)

/* YYTRANSLATE[TOKEN-NUM] -- Symbol number corresponding to TOKEN-NUM
   as returned by yylex, without out-of-bounds checking.  */
static const yytype_uint8 yytranslate[] =
{
       0,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     2,     2,     2,     2,
       2,     2,     2,     2,     2,     2,     1,     2,     3,     4,
       5,     6,     7,     8,     9,    10,    11,    12,    13,    14,
      15,    16,    17,    18,    19,    20,    21,    22,    23,    24,
      25,    26,    27,    28,    29,    30,    31,    32,    33,    34,
      35,    36,    37,    38,    39,    40,    41,    42,    43,    44,
      45,    46,    47,    48,    49,    50,    51,    52,    53,    54,
      55,    56,    57,    58,    59,    60,    61,    62,    63,    64,
      65,    66,    67,    68,    69,    70,    71,    72,    73
};

#if YYDEBUG
  /* YYRLINE[YYN] -- Source line where rule number YYN was defined.  */
static const yytype_uint16 yyrline[] =
{
       0,   103,   103,   108,   111,   117,   120,   123,   127,   132,
     137,   142,   146,   149,   152,   157,   164,   167,   172,   179,
     182,   185,   188,   191,   194,   197,   200,   203,   210,   215,
     222,   227,   235,   244,   253,   262,   267,   272,   277,   282,
     287,   292,   297,   302,   307,   312,   319,   326,   331,   342,
     345,   348,   351,   354,   357,   360,   365,   368,   373,   376,
     381,   384,   387,   390,   393,   396,   399,   402,   405,   408,
     411,   418,   421,   424,   427,   430,   433,   436,   439,   442,
     445,   448,   451,   458,   461,   466,   472,   475,   478,   481,
     484,   487,   490,   493,   498,   508,   511
};
#endif

#if YYDEBUG || YYERROR_VERBOSE || 0
/* YYTNAME[SYMBOL-NUM] -- String name of the symbol SYMBOL-NUM.
   First, the terminals, then, starting at YYNTOKENS, nonterminals.  */
static const char *const yytname[] =
{
  "$end", "error", "$undefined", "NUM_TOK", "G_TOK", "S_TOK", "ID_TOK",
  "ANGLE_TOK", "AUDIO_TOK", "BREAK_TOK", "BUTTON_TOK", "CALL_TOK",
  "CELL_TOK", "CHAPTER_TOK", "CLOSEBRACE_TOK", "CLOSEPAREN_TOK",
  "COUNTER_TOK", "ELSE_TOK", "ENTRY_TOK", "EXIT_TOK", "FPC_TOK",
  "GOTO_TOK", "IF_TOK", "JUMP_TOK", "MENU_TOK", "NEXT_TOK",
  "OPENBRACE_TOK", "OPENPAREN_TOK", "PGC_TOK", "PREV_TOK", "PROGRAM_TOK",
  "PTT_TOK", "REGION_TOK", "RESUME_TOK", "RND_TOK", "ROOT_TOK", "SET_TOK",
  "SUBTITLE_TOK", "TAIL_TOK", "TITLE_TOK", "TITLESET_TOK", "TOP_TOK",
  "UP_TOK", "VMGM_TOK", "_OR_TOK", "XOR_TOK", "LOR_TOK", "BOR_TOK",
  "_AND_TOK", "LAND_TOK", "BAND_TOK", "NOT_TOK", "EQ_TOK", "NE_TOK",
  "GE_TOK", "GT_TOK", "LE_TOK", "LT_TOK", "ADD_TOK", "SUB_TOK", "MUL_TOK",
  "DIV_TOK", "MOD_TOK", "ADDSET_TOK", "SUBSET_TOK", "MULSET_TOK",
  "DIVSET_TOK", "MODSET_TOK", "ANDSET_TOK", "ORSET_TOK", "XORSET_TOK",
  "SEMICOLON_TOK", "COLON_TOK", "ERROR_TOK", "$accept", "finalparse",
  "statements", "statement", "jtsl", "jtml", "jcl", "jumpstatement",
  "resumel", "callstatement", "reg", "regornum", "expression", "boolexpr",
  "regorcounter", "setstatement", "ifstatement", "ifelsestatement", YY_NULLPTR
};
#endif

# ifdef YYPRINT
/* YYTOKNUM[NUM] -- (External) token number corresponding to the
   (internal) symbol number NUM (which must be that of a token).  */
static const yytype_uint16 yytoknum[] =
{
       0,   256,   257,   258,   259,   260,   261,   262,   263,   264,
     265,   266,   267,   268,   269,   270,   271,   272,   273,   274,
     275,   276,   277,   278,   279,   280,   281,   282,   283,   284,
     285,   286,   287,   288,   289,   290,   291,   292,   293,   294,
     295,   296,   297,   298,   299,   300,   301,   302,   303,   304,
     305,   306,   307,   308,   309,   310,   311,   312,   313,   314,
     315,   316,   317,   318,   319,   320,   321,   322,   323,   324,
     325,   326,   327,   328
};
# endif

#define YYPACT_NINF -49

#define yypact_value_is_default(Yystate) \
  (!!((Yystate) == (-49)))

#define YYTABLE_NINF -1

#define yytable_value_is_error(Yytable_value) \
  0

  /* YYPACT[STATE-NUM] -- Index in YYTABLE of the portion describing
     STATE-NUM.  */
static const yytype_int16 yypact[] =
{
      21,   -49,   -49,   -48,   -49,   -49,   -33,   -49,    -4,    42,
     -22,    46,    28,   180,    21,   -49,   -15,   -49,    60,   -49,
      21,   -49,   -49,   363,    44,   -49,    74,   -49,   -49,   -49,
      93,   -49,    -5,   -49,   -49,    27,    82,     9,     5,     7,
      67,    10,    76,    -5,    91,   -49,   -49,   -49,   170,   170,
     170,   170,   170,   170,   170,   170,   170,    21,   -49,   -49,
      85,   104,    99,   -49,   -49,    82,    86,    82,   -49,   -49,
     339,    62,    49,    50,    51,    52,    55,    56,    59,    64,
      78,    88,   105,   108,   111,   113,    99,   -49,   170,   167,
     186,   205,   224,   243,   262,   281,   300,   319,   -49,   -49,
     159,   -49,   112,   123,    84,   137,   170,   -49,   170,   170,
     170,   170,   170,   170,   170,   170,   170,   170,   170,   170,
     170,   170,   170,   170,    21,    82,    82,    82,    82,   -49,
     -49,   -49,   -49,   -49,   -49,   -49,   -49,   -49,   -49,   -49,
     -49,   -49,   -49,   116,   103,   -49,   -49,   -49,   -49,   -49,
     -49,   -49,   -49,   -49,   -49,   -49,   -49,   -49,   -49,   -49,
     -49,   114,   117,   -49,   -49,   110,   362,   362,   362,   181,
     181,   357,   357,   357,   357,   357,   357,    40,    40,   -49,
     -49,   -49,   -49,    45,    45,   -49,   -49,   -49,   -49,   -49,
     -49
};

  /* YYDEFACT[STATE-NUM] -- Default reduction number in state STATE-NUM.
     Performed when YYTABLE does not specify something else to do.  Zero
     means the default is an error.  */
static const yytype_uint8 yydefact[] =
{
       0,    49,    50,     0,    53,    51,     0,    54,    17,     0,
       0,     0,     0,    17,     0,    55,     0,    52,     0,     2,
       3,     5,     6,    83,     0,    12,    95,    14,    10,    11,
       0,    16,    28,    84,     7,     0,     0,     0,     0,     0,
       0,     0,     0,    28,     0,     8,     1,     4,     0,     0,
       0,     0,     0,     0,     0,     0,     0,     0,    15,    26,
      19,     0,    30,     9,    57,     0,     0,     0,    56,    59,
       0,     0,     0,     0,     0,     0,     0,     0,     0,     0,
       0,     0,     0,     0,     0,     0,    30,    13,     0,     0,
       0,     0,     0,     0,     0,     0,     0,     0,    96,    18,
       0,    27,     0,    47,     0,     0,     0,    82,     0,     0,
       0,     0,     0,     0,     0,     0,     0,     0,     0,     0,
       0,     0,     0,     0,     0,     0,     0,     0,     0,    34,
      35,    36,    42,    39,    32,    45,    41,    37,    43,    40,
      33,    38,    44,     0,     0,    86,    87,    88,    89,    90,
      91,    92,    93,    85,    24,    23,    25,    21,    22,    20,
      29,     0,     0,    58,    71,     0,    68,    69,    66,    67,
      65,    72,    73,    74,    75,    76,    77,    60,    61,    62,
      63,    64,    94,    80,    78,    81,    79,    31,    46,    48,
      70
};

  /* YYPGOTO[NTERM-NUM].  */
static const yytype_int16 yypgoto[] =
{
     -49,   -49,     2,   -46,   176,   148,   107,   -49,   -49,   -49,
       0,   -49,   -47,   -44,   -49,   -49,   -49,   -49
};

  /* YYDEFGOTO[NTERM-NUM].  */
static const yytype_int16 yydefgoto[] =
{
      -1,    18,    19,    20,    32,    62,   103,    21,   162,    22,
      68,    69,    70,    71,    24,    25,    26,    27
};

  /* YYTABLE[YYPACT[STATE-NUM]] -- What to do in state STATE-NUM.  If
     positive, shift that token.  If negative, reduce the rule whose
     number is the opposite.  If YYTABLE_NINF, syntax error.  */
static const yytype_uint8 yytable[] =
{
      23,    89,    90,    91,    92,    93,    94,    95,    96,    97,
      77,    98,    72,    83,    23,    59,    44,    74,   104,    60,
      23,   105,    47,   107,    28,     1,     2,     3,     4,     5,
       6,     7,     8,    75,    61,    76,    30,     9,    29,    31,
      10,   144,    11,    12,    13,    78,    33,    14,    79,    34,
      73,    84,    35,    15,    16,    36,    45,    23,    17,   165,
      46,   166,   167,   168,   169,   170,   171,   172,   173,   174,
     175,   176,   177,   178,   179,   180,   181,   124,   182,    80,
      56,   183,   184,   185,   186,    64,     1,     2,    99,     4,
       5,    57,     7,   127,   128,    81,    58,    82,    63,   163,
     121,   122,   123,   100,    85,    87,   125,   101,   126,    65,
     127,   128,   102,   106,    15,   160,    66,   188,   163,    17,
     129,   130,   131,   132,    23,   190,   133,   134,   108,   109,
     135,   110,   111,    67,   112,   136,   113,   114,   115,   116,
     117,   118,   119,   120,   121,   122,   123,   108,   109,   137,
     110,   111,   164,   112,   108,   109,   161,   110,   111,   138,
     112,   119,   120,   121,   122,   123,   154,   155,   119,   120,
     121,   122,   123,    64,     1,     2,   139,     4,     5,   140,
       7,   125,   141,   126,   142,   127,   128,   187,   189,    43,
     156,    86,    37,   143,   157,     0,   158,    88,   159,     0,
       0,     0,    15,     0,    66,    38,     0,    17,    39,    40,
      41,   108,   109,     0,   110,   111,     0,   112,     0,     0,
      30,     0,    42,    31,     0,   119,   120,   121,   122,   123,
     108,   109,     0,   110,   111,     0,   112,     0,   145,   119,
     120,   121,   122,   123,   119,   120,   121,   122,   123,   108,
     109,     0,   110,   111,     0,   112,     0,   146,     0,     0,
       0,     0,     0,   119,   120,   121,   122,   123,   108,   109,
       0,   110,   111,     0,   112,     0,   147,     0,     0,     0,
       0,     0,   119,   120,   121,   122,   123,   108,   109,     0,
     110,   111,     0,   112,     0,   148,     0,     0,     0,     0,
       0,   119,   120,   121,   122,   123,   108,   109,     0,   110,
     111,     0,   112,     0,   149,     0,     0,     0,     0,     0,
     119,   120,   121,   122,   123,   108,   109,     0,   110,   111,
       0,   112,     0,   150,     0,     0,     0,     0,     0,   119,
     120,   121,   122,   123,   108,   109,     0,   110,   111,     0,
     112,     0,   151,     0,     0,     0,     0,     0,   119,   120,
     121,   122,   123,   108,   109,     0,   110,   111,     0,   112,
       0,   152,     0,     0,     0,     0,     0,   119,   120,   121,
     122,   123,     0,   108,   109,     0,   110,   111,     0,   112,
     153,   113,   114,   115,   116,   117,   118,   119,   120,   121,
     122,   123,   109,     0,   110,     0,     0,   112,     0,     0,
     111,     0,   112,     0,     0,   119,   120,   121,   122,   123,
     119,   120,   121,   122,   123,     0,    48,    49,    50,    51,
      52,    53,    54,    55
};

static const yytype_int16 yycheck[] =
{
       0,    48,    49,    50,    51,    52,    53,    54,    55,    56,
       3,    57,     3,     3,    14,    20,    14,    12,    65,    24,
      20,    65,    20,    67,    72,     4,     5,     6,     7,     8,
       9,    10,    11,    28,    39,    30,    40,    16,    71,    43,
      19,    88,    21,    22,    23,    38,     4,    26,    41,    71,
      41,    41,     6,    32,    33,    27,    71,    57,    37,   106,
       0,   108,   109,   110,   111,   112,   113,   114,   115,   116,
     117,   118,   119,   120,   121,   122,   123,    15,   124,    12,
      36,   125,   126,   127,   128,     3,     4,     5,     3,     7,
       8,    17,    10,    48,    49,    28,     3,    30,    71,    15,
      60,    61,    62,    18,    28,    14,    44,     3,    46,    27,
      48,    49,    13,    27,    32,     3,    34,     3,    15,    37,
      71,    71,    71,    71,   124,    15,    71,    71,    44,    45,
      71,    47,    48,    51,    50,    71,    52,    53,    54,    55,
      56,    57,    58,    59,    60,    61,    62,    44,    45,    71,
      47,    48,    15,    50,    44,    45,    33,    47,    48,    71,
      50,    58,    59,    60,    61,    62,     7,     8,    58,    59,
      60,    61,    62,     3,     4,     5,    71,     7,     8,    71,
      10,    44,    71,    46,    71,    48,    49,    71,    71,    13,
      31,    43,    12,    86,    35,    -1,    37,    27,    39,    -1,
      -1,    -1,    32,    -1,    34,    25,    -1,    37,    28,    29,
      30,    44,    45,    -1,    47,    48,    -1,    50,    -1,    -1,
      40,    -1,    42,    43,    -1,    58,    59,    60,    61,    62,
      44,    45,    -1,    47,    48,    -1,    50,    -1,    71,    58,
      59,    60,    61,    62,    58,    59,    60,    61,    62,    44,
      45,    -1,    47,    48,    -1,    50,    -1,    71,    -1,    -1,
      -1,    -1,    -1,    58,    59,    60,    61,    62,    44,    45,
      -1,    47,    48,    -1,    50,    -1,    71,    -1,    -1,    -1,
      -1,    -1,    58,    59,    60,    61,    62,    44,    45,    -1,
      47,    48,    -1,    50,    -1,    71,    -1,    -1,    -1,    -1,
      -1,    58,    59,    60,    61,    62,    44,    45,    -1,    47,
      48,    -1,    50,    -1,    71,    -1,    -1,    -1,    -1,    -1,
      58,    59,    60,    61,    62,    44,    45,    -1,    47,    48,
      -1,    50,    -1,    71,    -1,    -1,    -1,    -1,    -1,    58,
      59,    60,    61,    62,    44,    45,    -1,    47,    48,    -1,
      50,    -1,    71,    -1,    -1,    -1,    -1,    -1,    58,    59,
      60,    61,    62,    44,    45,    -1,    47,    48,    -1,    50,
      -1,    71,    -1,    -1,    -1,    -1,    -1,    58,    59,    60,
      61,    62,    -1,    44,    45,    -1,    47,    48,    -1,    50,
      71,    52,    53,    54,    55,    56,    57,    58,    59,    60,
      61,    62,    45,    -1,    47,    -1,    -1,    50,    -1,    -1,
      48,    -1,    50,    -1,    -1,    58,    59,    60,    61,    62,
      58,    59,    60,    61,    62,    -1,    63,    64,    65,    66,
      67,    68,    69,    70
};

  /* YYSTOS[STATE-NUM] -- The (internal number of the) accessing
     symbol of state STATE-NUM.  */
static const yytype_uint8 yystos[] =
{
       0,     4,     5,     6,     7,     8,     9,    10,    11,    16,
      19,    21,    22,    23,    26,    32,    33,    37,    75,    76,
      77,    81,    83,    84,    88,    89,    90,    91,    72,    71,
      40,    43,    78,     4,    71,     6,    27,    12,    25,    28,
      29,    30,    42,    78,    76,    71,     0,    76,    63,    64,
      65,    66,    67,    68,    69,    70,    36,    17,     3,    20,
      24,    39,    79,    71,     3,    27,    34,    51,    84,    85,
      86,    87,     3,    41,    12,    28,    30,     3,    38,    41,
      12,    28,    30,     3,    41,    28,    79,    14,    27,    86,
      86,    86,    86,    86,    86,    86,    86,    86,    77,     3,
      18,     3,    13,    80,    86,    87,    27,    87,    44,    45,
      47,    48,    50,    52,    53,    54,    55,    56,    57,    58,
      59,    60,    61,    62,    15,    44,    46,    48,    49,    71,
      71,    71,    71,    71,    71,    71,    71,    71,    71,    71,
      71,    71,    71,    80,    86,    71,    71,    71,    71,    71,
      71,    71,    71,    71,     7,     8,    31,    35,    37,    39,
       3,    33,    82,    15,    15,    86,    86,    86,    86,    86,
      86,    86,    86,    86,    86,    86,    86,    86,    86,    86,
      86,    86,    77,    87,    87,    87,    87,    71,     3,    71,
      15
};

  /* YYR1[YYN] -- Symbol number of symbol that rule YYN derives.  */
static const yytype_uint8 yyr1[] =
{
       0,    74,    75,    76,    76,    77,    77,    77,    77,    77,
      77,    77,    77,    77,    77,    78,    78,    78,    79,    79,
      79,    79,    79,    79,    79,    79,    79,    79,    79,    80,
      80,    81,    81,    81,    81,    81,    81,    81,    81,    81,
      81,    81,    81,    81,    81,    81,    82,    82,    83,    84,
      84,    84,    84,    84,    84,    84,    85,    85,    86,    86,
      86,    86,    86,    86,    86,    86,    86,    86,    86,    86,
      86,    87,    87,    87,    87,    87,    87,    87,    87,    87,
      87,    87,    87,    88,    88,    89,    89,    89,    89,    89,
      89,    89,    89,    89,    90,    91,    91
};

  /* YYR2[YYN] -- Number of symbols on the right hand side of rule YYN.  */
static const yytype_uint8 yyr2[] =
{
       0,     2,     1,     1,     2,     1,     1,     2,     2,     3,
       2,     2,     1,     3,     1,     2,     1,     0,     2,     1,
       3,     3,     3,     3,     3,     3,     1,     2,     0,     2,
       0,     5,     4,     4,     4,     4,     4,     4,     4,     4,
       4,     4,     4,     4,     4,     4,     2,     0,     6,     1,
       1,     1,     1,     1,     1,     1,     1,     1,     3,     1,
       3,     3,     3,     3,     3,     3,     3,     3,     3,     3,
       4,     3,     3,     3,     3,     3,     3,     3,     3,     3,
       3,     3,     2,     1,     2,     4,     4,     4,     4,     4,
       4,     4,     4,     4,     5,     1,     3
};


#define yyerrok         (yyerrstatus = 0)
#define yyclearin       (yychar = YYEMPTY)
#define YYEMPTY         (-2)
#define YYEOF           0

#define YYACCEPT        goto yyacceptlab
#define YYABORT         goto yyabortlab
#define YYERROR         goto yyerrorlab


#define YYRECOVERING()  (!!yyerrstatus)

#define YYBACKUP(Token, Value)                                  \
do                                                              \
  if (yychar == YYEMPTY)                                        \
    {                                                           \
      yychar = (Token);                                         \
      yylval = (Value);                                         \
      YYPOPSTACK (yylen);                                       \
      yystate = *yyssp;                                         \
      goto yybackup;                                            \
    }                                                           \
  else                                                          \
    {                                                           \
      yyerror (YY_("syntax error: cannot back up")); \
      YYERROR;                                                  \
    }                                                           \
while (0)

/* Error token number */
#define YYTERROR        1
#define YYERRCODE       256



/* Enable debugging if requested.  */
#if YYDEBUG

# ifndef YYFPRINTF
#  include <stdio.h> /* INFRINGES ON USER NAME SPACE */
#  define YYFPRINTF fprintf
# endif

# define YYDPRINTF(Args)                        \
do {                                            \
  if (yydebug)                                  \
    YYFPRINTF Args;                             \
} while (0)

/* This macro is provided for backward compatibility. */
#ifndef YY_LOCATION_PRINT
# define YY_LOCATION_PRINT(File, Loc) ((void) 0)
#endif


# define YY_SYMBOL_PRINT(Title, Type, Value, Location)                    \
do {                                                                      \
  if (yydebug)                                                            \
    {                                                                     \
      YYFPRINTF (stderr, "%s ", Title);                                   \
      yy_symbol_print (stderr,                                            \
                  Type, Value); \
      YYFPRINTF (stderr, "\n");                                           \
    }                                                                     \
} while (0)


/*----------------------------------------.
| Print this symbol's value on YYOUTPUT.  |
`----------------------------------------*/

static void
yy_symbol_value_print (FILE *yyoutput, int yytype, YYSTYPE const * const yyvaluep)
{
  FILE *yyo = yyoutput;
  YYUSE (yyo);
  if (!yyvaluep)
    return;
# ifdef YYPRINT
  if (yytype < YYNTOKENS)
    YYPRINT (yyoutput, yytoknum[yytype], *yyvaluep);
# endif
  YYUSE (yytype);
}


/*--------------------------------.
| Print this symbol on YYOUTPUT.  |
`--------------------------------*/

static void
yy_symbol_print (FILE *yyoutput, int yytype, YYSTYPE const * const yyvaluep)
{
  YYFPRINTF (yyoutput, "%s %s (",
             yytype < YYNTOKENS ? "token" : "nterm", yytname[yytype]);

  yy_symbol_value_print (yyoutput, yytype, yyvaluep);
  YYFPRINTF (yyoutput, ")");
}

/*------------------------------------------------------------------.
| yy_stack_print -- Print the state stack from its BOTTOM up to its |
| TOP (included).                                                   |
`------------------------------------------------------------------*/

static void
yy_stack_print (yytype_int16 *yybottom, yytype_int16 *yytop)
{
  YYFPRINTF (stderr, "Stack now");
  for (; yybottom <= yytop; yybottom++)
    {
      int yybot = *yybottom;
      YYFPRINTF (stderr, " %d", yybot);
    }
  YYFPRINTF (stderr, "\n");
}

# define YY_STACK_PRINT(Bottom, Top)                            \
do {                                                            \
  if (yydebug)                                                  \
    yy_stack_print ((Bottom), (Top));                           \
} while (0)


/*------------------------------------------------.
| Report that the YYRULE is going to be reduced.  |
`------------------------------------------------*/

static void
yy_reduce_print (yytype_int16 *yyssp, YYSTYPE *yyvsp, int yyrule)
{
  unsigned long int yylno = yyrline[yyrule];
  int yynrhs = yyr2[yyrule];
  int yyi;
  YYFPRINTF (stderr, "Reducing stack by rule %d (line %lu):\n",
             yyrule - 1, yylno);
  /* The symbols being reduced.  */
  for (yyi = 0; yyi < yynrhs; yyi++)
    {
      YYFPRINTF (stderr, "   $%d = ", yyi + 1);
      yy_symbol_print (stderr,
                       yystos[yyssp[yyi + 1 - yynrhs]],
                       &(yyvsp[(yyi + 1) - (yynrhs)])
                                              );
      YYFPRINTF (stderr, "\n");
    }
}

# define YY_REDUCE_PRINT(Rule)          \
do {                                    \
  if (yydebug)                          \
    yy_reduce_print (yyssp, yyvsp, Rule); \
} while (0)

/* Nonzero means print parse trace.  It is left uninitialized so that
   multiple parsers can coexist.  */
int yydebug;
#else /* !YYDEBUG */
# define YYDPRINTF(Args)
# define YY_SYMBOL_PRINT(Title, Type, Value, Location)
# define YY_STACK_PRINT(Bottom, Top)
# define YY_REDUCE_PRINT(Rule)
#endif /* !YYDEBUG */


/* YYINITDEPTH -- initial size of the parser's stacks.  */
#ifndef YYINITDEPTH
# define YYINITDEPTH 200
#endif

/* YYMAXDEPTH -- maximum size the stacks can grow to (effective only
   if the built-in stack extension method is used).

   Do not make this value too large; the results are undefined if
   YYSTACK_ALLOC_MAXIMUM < YYSTACK_BYTES (YYMAXDEPTH)
   evaluated with infinite-precision integer arithmetic.  */

#ifndef YYMAXDEPTH
# define YYMAXDEPTH 10000
#endif


#if YYERROR_VERBOSE

# ifndef yystrlen
#  if defined __GLIBC__ && defined _STRING_H
#   define yystrlen strlen
#  else
/* Return the length of YYSTR.  */
static YYSIZE_T
yystrlen (const char *yystr)
{
  YYSIZE_T yylen;
  for (yylen = 0; yystr[yylen]; yylen++)
    continue;
  return yylen;
}
#  endif
# endif

# ifndef yystpcpy
#  if defined __GLIBC__ && defined _STRING_H && defined _GNU_SOURCE
#   define yystpcpy stpcpy
#  else
/* Copy YYSRC to YYDEST, returning the address of the terminating '\0' in
   YYDEST.  */
static char *
yystpcpy (char *yydest, const char *yysrc)
{
  char *yyd = yydest;
  const char *yys = yysrc;

  while ((*yyd++ = *yys++) != '\0')
    continue;

  return yyd - 1;
}
#  endif
# endif

# ifndef yytnamerr
/* Copy to YYRES the contents of YYSTR after stripping away unnecessary
   quotes and backslashes, so that it's suitable for yyerror.  The
   heuristic is that double-quoting is unnecessary unless the string
   contains an apostrophe, a comma, or backslash (other than
   backslash-backslash).  YYSTR is taken from yytname.  If YYRES is
   null, do not copy; instead, return the length of what the result
   would have been.  */
static YYSIZE_T
yytnamerr (char *yyres, const char *yystr)
{
  if (*yystr == '"')
    {
      YYSIZE_T yyn = 0;
      char const *yyp = yystr;

      for (;;)
        switch (*++yyp)
          {
          case '\'':
          case ',':
            goto do_not_strip_quotes;

          case '\\':
            if (*++yyp != '\\')
              goto do_not_strip_quotes;
            /* Fall through.  */
          default:
            if (yyres)
              yyres[yyn] = *yyp;
            yyn++;
            break;

          case '"':
            if (yyres)
              yyres[yyn] = '\0';
            return yyn;
          }
    do_not_strip_quotes: ;
    }

  if (! yyres)
    return yystrlen (yystr);

  return yystpcpy (yyres, yystr) - yyres;
}
# endif

/* Copy into *YYMSG, which is of size *YYMSG_ALLOC, an error message
   about the unexpected token YYTOKEN for the state stack whose top is
   YYSSP.

   Return 0 if *YYMSG was successfully written.  Return 1 if *YYMSG is
   not large enough to hold the message.  In that case, also set
   *YYMSG_ALLOC to the required number of bytes.  Return 2 if the
   required number of bytes is too large to store.  */
static int
yysyntax_error (YYSIZE_T *yymsg_alloc, char **yymsg,
                yytype_int16 *yyssp, int yytoken)
{
  YYSIZE_T yysize0 = yytnamerr (YY_NULLPTR, yytname[yytoken]);
  YYSIZE_T yysize = yysize0;
  enum { YYERROR_VERBOSE_ARGS_MAXIMUM = 5 };
  /* Internationalized format string. */
  const char *yyformat = YY_NULLPTR;
  /* Arguments of yyformat. */
  char const *yyarg[YYERROR_VERBOSE_ARGS_MAXIMUM];
  /* Number of reported tokens (one for the "unexpected", one per
     "expected"). */
  int yycount = 0;

  /* There are many possibilities here to consider:
     - If this state is a consistent state with a default action, then
       the only way this function was invoked is if the default action
       is an error action.  In that case, don't check for expected
       tokens because there are none.
     - The only way there can be no lookahead present (in yychar) is if
       this state is a consistent state with a default action.  Thus,
       detecting the absence of a lookahead is sufficient to determine
       that there is no unexpected or expected token to report.  In that
       case, just report a simple "syntax error".
     - Don't assume there isn't a lookahead just because this state is a
       consistent state with a default action.  There might have been a
       previous inconsistent state, consistent state with a non-default
       action, or user semantic action that manipulated yychar.
     - Of course, the expected token list depends on states to have
       correct lookahead information, and it depends on the parser not
       to perform extra reductions after fetching a lookahead from the
       scanner and before detecting a syntax error.  Thus, state merging
       (from LALR or IELR) and default reductions corrupt the expected
       token list.  However, the list is correct for canonical LR with
       one exception: it will still contain any token that will not be
       accepted due to an error action in a later state.
  */
  if (yytoken != YYEMPTY)
    {
      int yyn = yypact[*yyssp];
      yyarg[yycount++] = yytname[yytoken];
      if (!yypact_value_is_default (yyn))
        {
          /* Start YYX at -YYN if negative to avoid negative indexes in
             YYCHECK.  In other words, skip the first -YYN actions for
             this state because they are default actions.  */
          int yyxbegin = yyn < 0 ? -yyn : 0;
          /* Stay within bounds of both yycheck and yytname.  */
          int yychecklim = YYLAST - yyn + 1;
          int yyxend = yychecklim < YYNTOKENS ? yychecklim : YYNTOKENS;
          int yyx;

          for (yyx = yyxbegin; yyx < yyxend; ++yyx)
            if (yycheck[yyx + yyn] == yyx && yyx != YYTERROR
                && !yytable_value_is_error (yytable[yyx + yyn]))
              {
                if (yycount == YYERROR_VERBOSE_ARGS_MAXIMUM)
                  {
                    yycount = 1;
                    yysize = yysize0;
                    break;
                  }
                yyarg[yycount++] = yytname[yyx];
                {
                  YYSIZE_T yysize1 = yysize + yytnamerr (YY_NULLPTR, yytname[yyx]);
                  if (! (yysize <= yysize1
                         && yysize1 <= YYSTACK_ALLOC_MAXIMUM))
                    return 2;
                  yysize = yysize1;
                }
              }
        }
    }

  switch (yycount)
    {
# define YYCASE_(N, S)                      \
      case N:                               \
        yyformat = S;                       \
      break
      YYCASE_(0, YY_("syntax error"));
      YYCASE_(1, YY_("syntax error, unexpected %s"));
      YYCASE_(2, YY_("syntax error, unexpected %s, expecting %s"));
      YYCASE_(3, YY_("syntax error, unexpected %s, expecting %s or %s"));
      YYCASE_(4, YY_("syntax error, unexpected %s, expecting %s or %s or %s"));
      YYCASE_(5, YY_("syntax error, unexpected %s, expecting %s or %s or %s or %s"));
# undef YYCASE_
    }

  {
    YYSIZE_T yysize1 = yysize + yystrlen (yyformat);
    if (! (yysize <= yysize1 && yysize1 <= YYSTACK_ALLOC_MAXIMUM))
      return 2;
    yysize = yysize1;
  }

  if (*yymsg_alloc < yysize)
    {
      *yymsg_alloc = 2 * yysize;
      if (! (yysize <= *yymsg_alloc
             && *yymsg_alloc <= YYSTACK_ALLOC_MAXIMUM))
        *yymsg_alloc = YYSTACK_ALLOC_MAXIMUM;
      return 1;
    }

  /* Avoid sprintf, as that infringes on the user's name space.
     Don't have undefined behavior even if the translation
     produced a string with the wrong number of "%s"s.  */
  {
    char *yyp = *yymsg;
    int yyi = 0;
    while ((*yyp = *yyformat) != '\0')
      if (*yyp == '%' && yyformat[1] == 's' && yyi < yycount)
        {
          yyp += yytnamerr (yyp, yyarg[yyi++]);
          yyformat += 2;
        }
      else
        {
          yyp++;
          yyformat++;
        }
  }
  return 0;
}
#endif /* YYERROR_VERBOSE */

/*-----------------------------------------------.
| Release the memory associated to this symbol.  |
`-----------------------------------------------*/

static void
yydestruct (const char *yymsg, int yytype, YYSTYPE *yyvaluep)
{
  YYUSE (yyvaluep);
  if (!yymsg)
    yymsg = "Deleting";
  YY_SYMBOL_PRINT (yymsg, yytype, yyvaluep, yylocationp);

  YY_IGNORE_MAYBE_UNINITIALIZED_BEGIN
  YYUSE (yytype);
  YY_IGNORE_MAYBE_UNINITIALIZED_END
}




/* The lookahead symbol.  */
int yychar;

/* The semantic value of the lookahead symbol.  */
YYSTYPE yylval;
/* Number of syntax errors so far.  */
int yynerrs;


/*----------.
| yyparse.  |
`----------*/

int
yyparse (void)
{
    int yystate;
    /* Number of tokens to shift before error messages enabled.  */
    int yyerrstatus;

    /* The stacks and their tools:
       'yyss': related to states.
       'yyvs': related to semantic values.

       Refer to the stacks through separate pointers, to allow yyoverflow
       to reallocate them elsewhere.  */

    /* The state stack.  */
    yytype_int16 yyssa[YYINITDEPTH];
    yytype_int16 *yyss;
    yytype_int16 *yyssp;

    /* The semantic value stack.  */
    YYSTYPE yyvsa[YYINITDEPTH];
    YYSTYPE *yyvs;
    YYSTYPE *yyvsp;

    YYSIZE_T yystacksize;

  int yyn;
  int yyresult;
  /* Lookahead token as an internal (translated) token number.  */
  int yytoken = 0;
  /* The variables used to return semantic value and location from the
     action routines.  */
  YYSTYPE yyval;

#if YYERROR_VERBOSE
  /* Buffer for error messages, and its allocated size.  */
  char yymsgbuf[128];
  char *yymsg = yymsgbuf;
  YYSIZE_T yymsg_alloc = sizeof yymsgbuf;
#endif

#define YYPOPSTACK(N)   (yyvsp -= (N), yyssp -= (N))

  /* The number of symbols on the RHS of the reduced rule.
     Keep to zero when no symbol should be popped.  */
  int yylen = 0;

  yyssp = yyss = yyssa;
  yyvsp = yyvs = yyvsa;
  yystacksize = YYINITDEPTH;

  YYDPRINTF ((stderr, "Starting parse\n"));

  yystate = 0;
  yyerrstatus = 0;
  yynerrs = 0;
  yychar = YYEMPTY; /* Cause a token to be read.  */
  goto yysetstate;

/*------------------------------------------------------------.
| yynewstate -- Push a new state, which is found in yystate.  |
`------------------------------------------------------------*/
 yynewstate:
  /* In all cases, when you get here, the value and location stacks
     have just been pushed.  So pushing a state here evens the stacks.  */
  yyssp++;

 yysetstate:
  *yyssp = yystate;

  if (yyss + yystacksize - 1 <= yyssp)
    {
      /* Get the current used size of the three stacks, in elements.  */
      YYSIZE_T yysize = yyssp - yyss + 1;

#ifdef yyoverflow
      {
        /* Give user a chance to reallocate the stack.  Use copies of
           these so that the &'s don't force the real ones into
           memory.  */
        YYSTYPE *yyvs1 = yyvs;
        yytype_int16 *yyss1 = yyss;

        /* Each stack pointer address is followed by the size of the
           data in use in that stack, in bytes.  This used to be a
           conditional around just the two extra args, but that might
           be undefined if yyoverflow is a macro.  */
        yyoverflow (YY_("memory exhausted"),
                    &yyss1, yysize * sizeof (*yyssp),
                    &yyvs1, yysize * sizeof (*yyvsp),
                    &yystacksize);

        yyss = yyss1;
        yyvs = yyvs1;
      }
#else /* no yyoverflow */
# ifndef YYSTACK_RELOCATE
      goto yyexhaustedlab;
# else
      /* Extend the stack our own way.  */
      if (YYMAXDEPTH <= yystacksize)
        goto yyexhaustedlab;
      yystacksize *= 2;
      if (YYMAXDEPTH < yystacksize)
        yystacksize = YYMAXDEPTH;

      {
        yytype_int16 *yyss1 = yyss;
        union yyalloc *yyptr =
          (union yyalloc *) YYSTACK_ALLOC (YYSTACK_BYTES (yystacksize));
        if (! yyptr)
          goto yyexhaustedlab;
        YYSTACK_RELOCATE (yyss_alloc, yyss);
        YYSTACK_RELOCATE (yyvs_alloc, yyvs);
#  undef YYSTACK_RELOCATE
        if (yyss1 != yyssa)
          YYSTACK_FREE (yyss1);
      }
# endif
#endif /* no yyoverflow */

      yyssp = yyss + yysize - 1;
      yyvsp = yyvs + yysize - 1;

      YYDPRINTF ((stderr, "Stack size increased to %lu\n",
                  (unsigned long int) yystacksize));

      if (yyss + yystacksize - 1 <= yyssp)
        YYABORT;
    }

  YYDPRINTF ((stderr, "Entering state %d\n", yystate));

  if (yystate == YYFINAL)
    YYACCEPT;

  goto yybackup;

/*-----------.
| yybackup.  |
`-----------*/
yybackup:

  /* Do appropriate processing given the current state.  Read a
     lookahead token if we need one and don't already have one.  */

  /* First try to decide what to do without reference to lookahead token.  */
  yyn = yypact[yystate];
  if (yypact_value_is_default (yyn))
    goto yydefault;

  /* Not known => get a lookahead token if don't already have one.  */

  /* YYCHAR is either YYEMPTY or YYEOF or a valid lookahead symbol.  */
  if (yychar == YYEMPTY)
    {
      YYDPRINTF ((stderr, "Reading a token: "));
      yychar = yylex ();
    }

  if (yychar <= YYEOF)
    {
      yychar = yytoken = YYEOF;
      YYDPRINTF ((stderr, "Now at end of input.\n"));
    }
  else
    {
      yytoken = YYTRANSLATE (yychar);
      YY_SYMBOL_PRINT ("Next token is", yytoken, &yylval, &yylloc);
    }

  /* If the proper action on seeing token YYTOKEN is to reduce or to
     detect an error, take that action.  */
  yyn += yytoken;
  if (yyn < 0 || YYLAST < yyn || yycheck[yyn] != yytoken)
    goto yydefault;
  yyn = yytable[yyn];
  if (yyn <= 0)
    {
      if (yytable_value_is_error (yyn))
        goto yyerrlab;
      yyn = -yyn;
      goto yyreduce;
    }

  /* Count tokens shifted since error; after three, turn off error
     status.  */
  if (yyerrstatus)
    yyerrstatus--;

  /* Shift the lookahead token.  */
  YY_SYMBOL_PRINT ("Shifting", yytoken, &yylval, &yylloc);

  /* Discard the shifted token.  */
  yychar = YYEMPTY;

  yystate = yyn;
  YY_IGNORE_MAYBE_UNINITIALIZED_BEGIN
  *++yyvsp = yylval;
  YY_IGNORE_MAYBE_UNINITIALIZED_END

  goto yynewstate;


/*-----------------------------------------------------------.
| yydefault -- do the default action for the current state.  |
`-----------------------------------------------------------*/
yydefault:
  yyn = yydefact[yystate];
  if (yyn == 0)
    goto yyerrlab;
  goto yyreduce;


/*-----------------------------.
| yyreduce -- Do a reduction.  |
`-----------------------------*/
yyreduce:
  /* yyn is the number of a rule to reduce with.  */
  yylen = yyr2[yyn];

  /* If YYLEN is nonzero, implement the default value of the action:
     '$$ = $1'.

     Otherwise, the following line sets YYVAL to garbage.
     This behavior is undocumented and Bison
     users should not rely upon it.  Assigning to YYVAL
     unconditionally makes the parser a bit smaller, and it avoids a
     GCC warning that YYVAL may be used uninitialized.  */
  yyval = yyvsp[1-yylen];


  YY_REDUCE_PRINT (yyn);
  switch (yyn)
    {
        case 2:
#line 103 "dvdvmy.y" /* yacc.c:1646  */
    {
    dvd_vm_parsed_cmd=(yyval.statement);
}
#line 1509 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 3:
#line 108 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=(yyvsp[0].statement);
}
#line 1517 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 4:
#line 111 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=(yyvsp[-1].statement);
    (yyval.statement)->next=(yyvsp[0].statement);
}
#line 1526 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 5:
#line 117 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=(yyvsp[0].statement);
}
#line 1534 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 6:
#line 120 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=(yyvsp[0].statement);
}
#line 1542 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 7:
#line 123 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_EXIT;
}
#line 1551 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 8:
#line 127 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_LINK;
    (yyval.statement)->i1=16;
}
#line 1561 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 9:
#line 132 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_GOTO;
    (yyval.statement)->s1=(yyvsp[-1].str_val);
}
#line 1571 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 10:
#line 137 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_LABEL;
    (yyval.statement)->s1=(yyvsp[-1].str_val);
}
#line 1581 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 11:
#line 142 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_BREAK;
}
#line 1590 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 12:
#line 146 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=(yyvsp[0].statement);
}
#line 1598 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 13:
#line 149 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=(yyvsp[-1].statement);
}
#line 1606 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 14:
#line 152 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=(yyvsp[0].statement);
}
#line 1614 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 15:
#line 157 "dvdvmy.y" /* yacc.c:1646  */
    {
    if ((yyvsp[0].int_val) < 1 || (yyvsp[0].int_val) > 99)
      {
        yyerror("titleset number out of range");
      } /*if*/
    (yyval.int_val)=((yyvsp[0].int_val))+1;
}
#line 1626 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 16:
#line 164 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=1;
}
#line 1634 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 17:
#line 167 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=0;
}
#line 1642 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 18:
#line 172 "dvdvmy.y" /* yacc.c:1646  */
    {
    if ((yyvsp[0].int_val) < 1 || (yyvsp[0].int_val) > 99)
      {
        yyerror("menu number out of range");
      } /*if*/
    (yyval.int_val)=(yyvsp[0].int_val);
}
#line 1654 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 19:
#line 179 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=120; // default entry
}
#line 1662 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 20:
#line 182 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=122;
}
#line 1670 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 21:
#line 185 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=123;
}
#line 1678 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 22:
#line 188 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=124;
}
#line 1686 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 23:
#line 191 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=125;
}
#line 1694 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 24:
#line 194 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=126;
}
#line 1702 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 25:
#line 197 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=127;
}
#line 1710 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 26:
#line 200 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=121;
}
#line 1718 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 27:
#line 203 "dvdvmy.y" /* yacc.c:1646  */
    {
    if ((yyvsp[0].int_val) < 1 || (yyvsp[0].int_val) > 99)
      {
        yyerror("title number out of range");
      } /*if*/
    (yyval.int_val)=((yyvsp[0].int_val))|128;
}
#line 1730 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 28:
#line 210 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=0;
}
#line 1738 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 29:
#line 215 "dvdvmy.y" /* yacc.c:1646  */
    {
    if ((yyvsp[0].int_val) < 1 || (yyvsp[0].int_val) > 65535)
      {
        yyerror("chapter number out of range");
      } /*if*/
    (yyval.int_val)=(yyvsp[0].int_val);
}
#line 1750 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 30:
#line 222 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=0;
}
#line 1758 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 31:
#line 227 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_JUMP;
  /* values already range-checked: */
    (yyval.statement)->i1=(yyvsp[-3].int_val);
    (yyval.statement)->i2=(yyvsp[-2].int_val);
    (yyval.statement)->i3=1*65536+(yyvsp[-1].int_val);
}
#line 1771 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 32:
#line 235 "dvdvmy.y" /* yacc.c:1646  */
    {
    if ((yyvsp[-1].int_val) < 1 || (yyvsp[-1].int_val) > 65535)
      {
        yyerror("PGC number out of range");
      } /*if*/
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_JUMP;
    (yyval.statement)->i3=0*65536+(yyvsp[-1].int_val);
}
#line 1785 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 33:
#line 244 "dvdvmy.y" /* yacc.c:1646  */
    {
    if ((yyvsp[-1].int_val) < 1 || (yyvsp[-1].int_val) > 65535)
      {
        yyerror("program number out of range");
      } /*if*/
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_JUMP;
    (yyval.statement)->i3=2*65536+(yyvsp[-1].int_val);
}
#line 1799 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 34:
#line 253 "dvdvmy.y" /* yacc.c:1646  */
    {
    if ((yyvsp[-1].int_val) < 1 || (yyvsp[-1].int_val) > 65535)
      {
        yyerror("cell number out of range");
      } /*if*/
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_JUMP;
    (yyval.statement)->i3=3*65536+(yyvsp[-1].int_val);
}
#line 1813 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 35:
#line 262 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_LINK;
    (yyval.statement)->i1=1;
}
#line 1823 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 36:
#line 267 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_LINK;
    (yyval.statement)->i1=2;
}
#line 1833 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 37:
#line 272 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_LINK;
    (yyval.statement)->i1=3;
}
#line 1843 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 38:
#line 277 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_LINK;
    (yyval.statement)->i1=5;
}
#line 1853 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 39:
#line 282 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_LINK;
    (yyval.statement)->i1=6;
}
#line 1863 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 40:
#line 287 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_LINK;
    (yyval.statement)->i1=7;
}
#line 1873 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 41:
#line 292 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_LINK;
    (yyval.statement)->i1=9;
}
#line 1883 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 42:
#line 297 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_LINK;
    (yyval.statement)->i1=10;
}
#line 1893 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 43:
#line 302 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_LINK;
    (yyval.statement)->i1=11;
}
#line 1903 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 44:
#line 307 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_LINK;
    (yyval.statement)->i1=12;
}
#line 1913 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 45:
#line 312 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_LINK;
    (yyval.statement)->i1=13;
}
#line 1923 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 46:
#line 319 "dvdvmy.y" /* yacc.c:1646  */
    {
    if ((yyvsp[0].int_val) < 1 || (yyvsp[0].int_val) > 65535)
      {
        yyerror("resume cell number out of range");
      } /*if*/
    (yyval.int_val)=(yyvsp[0].int_val);
}
#line 1935 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 47:
#line 326 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=0;
}
#line 1943 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 48:
#line 331 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_CALL;
  /* values already range-checked: */
    (yyval.statement)->i1=(yyvsp[-4].int_val);
    (yyval.statement)->i2=(yyvsp[-3].int_val);
    (yyval.statement)->i3=(yyvsp[-2].int_val);
    (yyval.statement)->i4=(yyvsp[-1].int_val);
}
#line 1957 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 49:
#line 342 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=(yyvsp[0].int_val);
}
#line 1965 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 50:
#line 345 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=(yyvsp[0].int_val)+0x80;
}
#line 1973 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 51:
#line 348 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=0x81;
}
#line 1981 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 52:
#line 351 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=0x82;
}
#line 1989 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 53:
#line 354 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=0x83;
}
#line 1997 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 54:
#line 357 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=0x88;
}
#line 2005 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 55:
#line 360 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=0x80+20;
}
#line 2013 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 56:
#line 365 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=(yyvsp[0].int_val)-256;
}
#line 2021 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 57:
#line 368 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=(yyvsp[0].int_val);
}
#line 2029 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 58:
#line 373 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=(yyvsp[-1].statement);
}
#line 2037 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 59:
#line 376 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_VAL;
    (yyval.statement)->i1=(yyvsp[0].int_val);
}
#line 2047 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 60:
#line 381 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_ADD,(yyvsp[0].statement));
}
#line 2055 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 61:
#line 384 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_SUB,(yyvsp[0].statement));
}
#line 2063 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 62:
#line 387 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_MUL,(yyvsp[0].statement));
}
#line 2071 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 63:
#line 390 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_DIV,(yyvsp[0].statement));
}
#line 2079 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 64:
#line 393 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_MOD,(yyvsp[0].statement));
}
#line 2087 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 65:
#line 396 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_AND,(yyvsp[0].statement));
}
#line 2095 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 66:
#line 399 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_OR, (yyvsp[0].statement));
}
#line 2103 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 67:
#line 402 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_AND,(yyvsp[0].statement));
}
#line 2111 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 68:
#line 405 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_OR, (yyvsp[0].statement));
}
#line 2119 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 69:
#line 408 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_XOR,(yyvsp[0].statement));
}
#line 2127 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 70:
#line 411 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_RND;
    (yyval.statement)->param=(yyvsp[-1].statement);
}
#line 2137 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 71:
#line 418 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=(yyvsp[-1].statement);
}
#line 2145 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 72:
#line 421 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_EQ,(yyvsp[0].statement));
}
#line 2153 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 73:
#line 424 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_NE,(yyvsp[0].statement));
}
#line 2161 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 74:
#line 427 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_GTE,(yyvsp[0].statement));
}
#line 2169 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 75:
#line 430 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_GT,(yyvsp[0].statement));
}
#line 2177 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 76:
#line 433 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_LTE,(yyvsp[0].statement));
}
#line 2185 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 77:
#line 436 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_LT,(yyvsp[0].statement));
}
#line 2193 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 78:
#line 439 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_LOR,(yyvsp[0].statement));
}
#line 2201 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 79:
#line 442 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_LAND,(yyvsp[0].statement));
}
#line 2209 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 80:
#line 445 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_LOR,(yyvsp[0].statement));
}
#line 2217 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 81:
#line 448 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_expression((yyvsp[-2].statement),VM_LAND,(yyvsp[0].statement));
}
#line 2225 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 82:
#line 451 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_NOT;
    (yyval.statement)->param=(yyvsp[0].statement);
}
#line 2235 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 83:
#line 458 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=(yyvsp[0].int_val);
}
#line 2243 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 84:
#line 461 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.int_val)=(yyvsp[0].int_val)+0x20;
}
#line 2251 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 85:
#line 466 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_SET;
    (yyval.statement)->i1=(yyvsp[-3].int_val);
    (yyval.statement)->param=(yyvsp[-1].statement);
}
#line 2262 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 86:
#line 472 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_setop((yyvsp[-3].int_val),VM_ADD,(yyvsp[-1].statement));
}
#line 2270 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 87:
#line 475 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_setop((yyvsp[-3].int_val),VM_SUB,(yyvsp[-1].statement));
}
#line 2278 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 88:
#line 478 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_setop((yyvsp[-3].int_val),VM_MUL,(yyvsp[-1].statement));
}
#line 2286 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 89:
#line 481 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_setop((yyvsp[-3].int_val),VM_DIV,(yyvsp[-1].statement));
}
#line 2294 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 90:
#line 484 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_setop((yyvsp[-3].int_val),VM_MOD,(yyvsp[-1].statement));
}
#line 2302 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 91:
#line 487 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_setop((yyvsp[-3].int_val),VM_AND,(yyvsp[-1].statement));
}
#line 2310 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 92:
#line 490 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_setop((yyvsp[-3].int_val),VM_OR,(yyvsp[-1].statement));
}
#line 2318 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 93:
#line 493 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_setop((yyvsp[-3].int_val),VM_XOR,(yyvsp[-1].statement));
}
#line 2326 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 94:
#line 498 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=statement_new();
    (yyval.statement)->op=VM_IF;
    (yyval.statement)->param=(yyvsp[-2].statement);
    (yyvsp[-2].statement)->next=statement_new();
    (yyvsp[-2].statement)->next->op=VM_IF;
    (yyvsp[-2].statement)->next->param=(yyvsp[0].statement);
}
#line 2339 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 95:
#line 508 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=(yyvsp[0].statement);
}
#line 2347 "dvdvmy.c" /* yacc.c:1646  */
    break;

  case 96:
#line 511 "dvdvmy.y" /* yacc.c:1646  */
    {
    (yyval.statement)=(yyvsp[-2].statement);
    (yyval.statement)->param->next->next=(yyvsp[0].statement);
}
#line 2356 "dvdvmy.c" /* yacc.c:1646  */
    break;


#line 2360 "dvdvmy.c" /* yacc.c:1646  */
      default: break;
    }
  /* User semantic actions sometimes alter yychar, and that requires
     that yytoken be updated with the new translation.  We take the
     approach of translating immediately before every use of yytoken.
     One alternative is translating here after every semantic action,
     but that translation would be missed if the semantic action invokes
     YYABORT, YYACCEPT, or YYERROR immediately after altering yychar or
     if it invokes YYBACKUP.  In the case of YYABORT or YYACCEPT, an
     incorrect destructor might then be invoked immediately.  In the
     case of YYERROR or YYBACKUP, subsequent parser actions might lead
     to an incorrect destructor call or verbose syntax error message
     before the lookahead is translated.  */
  YY_SYMBOL_PRINT ("-> $$ =", yyr1[yyn], &yyval, &yyloc);

  YYPOPSTACK (yylen);
  yylen = 0;
  YY_STACK_PRINT (yyss, yyssp);

  *++yyvsp = yyval;

  /* Now 'shift' the result of the reduction.  Determine what state
     that goes to, based on the state we popped back to and the rule
     number reduced by.  */

  yyn = yyr1[yyn];

  yystate = yypgoto[yyn - YYNTOKENS] + *yyssp;
  if (0 <= yystate && yystate <= YYLAST && yycheck[yystate] == *yyssp)
    yystate = yytable[yystate];
  else
    yystate = yydefgoto[yyn - YYNTOKENS];

  goto yynewstate;


/*--------------------------------------.
| yyerrlab -- here on detecting error.  |
`--------------------------------------*/
yyerrlab:
  /* Make sure we have latest lookahead translation.  See comments at
     user semantic actions for why this is necessary.  */
  yytoken = yychar == YYEMPTY ? YYEMPTY : YYTRANSLATE (yychar);

  /* If not already recovering from an error, report this error.  */
  if (!yyerrstatus)
    {
      ++yynerrs;
#if ! YYERROR_VERBOSE
      yyerror (YY_("syntax error"));
#else
# define YYSYNTAX_ERROR yysyntax_error (&yymsg_alloc, &yymsg, \
                                        yyssp, yytoken)
      {
        char const *yymsgp = YY_("syntax error");
        int yysyntax_error_status;
        yysyntax_error_status = YYSYNTAX_ERROR;
        if (yysyntax_error_status == 0)
          yymsgp = yymsg;
        else if (yysyntax_error_status == 1)
          {
            if (yymsg != yymsgbuf)
              YYSTACK_FREE (yymsg);
            yymsg = (char *) YYSTACK_ALLOC (yymsg_alloc);
            if (!yymsg)
              {
                yymsg = yymsgbuf;
                yymsg_alloc = sizeof yymsgbuf;
                yysyntax_error_status = 2;
              }
            else
              {
                yysyntax_error_status = YYSYNTAX_ERROR;
                yymsgp = yymsg;
              }
          }
        yyerror (yymsgp);
        if (yysyntax_error_status == 2)
          goto yyexhaustedlab;
      }
# undef YYSYNTAX_ERROR
#endif
    }



  if (yyerrstatus == 3)
    {
      /* If just tried and failed to reuse lookahead token after an
         error, discard it.  */

      if (yychar <= YYEOF)
        {
          /* Return failure if at end of input.  */
          if (yychar == YYEOF)
            YYABORT;
        }
      else
        {
          yydestruct ("Error: discarding",
                      yytoken, &yylval);
          yychar = YYEMPTY;
        }
    }

  /* Else will try to reuse lookahead token after shifting the error
     token.  */
  goto yyerrlab1;


/*---------------------------------------------------.
| yyerrorlab -- error raised explicitly by YYERROR.  |
`---------------------------------------------------*/
yyerrorlab:

  /* Pacify compilers like GCC when the user code never invokes
     YYERROR and the label yyerrorlab therefore never appears in user
     code.  */
  if (/*CONSTCOND*/ 0)
     goto yyerrorlab;

  /* Do not reclaim the symbols of the rule whose action triggered
     this YYERROR.  */
  YYPOPSTACK (yylen);
  yylen = 0;
  YY_STACK_PRINT (yyss, yyssp);
  yystate = *yyssp;
  goto yyerrlab1;


/*-------------------------------------------------------------.
| yyerrlab1 -- common code for both syntax error and YYERROR.  |
`-------------------------------------------------------------*/
yyerrlab1:
  yyerrstatus = 3;      /* Each real token shifted decrements this.  */

  for (;;)
    {
      yyn = yypact[yystate];
      if (!yypact_value_is_default (yyn))
        {
          yyn += YYTERROR;
          if (0 <= yyn && yyn <= YYLAST && yycheck[yyn] == YYTERROR)
            {
              yyn = yytable[yyn];
              if (0 < yyn)
                break;
            }
        }

      /* Pop the current state because it cannot handle the error token.  */
      if (yyssp == yyss)
        YYABORT;


      yydestruct ("Error: popping",
                  yystos[yystate], yyvsp);
      YYPOPSTACK (1);
      yystate = *yyssp;
      YY_STACK_PRINT (yyss, yyssp);
    }

  YY_IGNORE_MAYBE_UNINITIALIZED_BEGIN
  *++yyvsp = yylval;
  YY_IGNORE_MAYBE_UNINITIALIZED_END


  /* Shift the error token.  */
  YY_SYMBOL_PRINT ("Shifting", yystos[yyn], yyvsp, yylsp);

  yystate = yyn;
  goto yynewstate;


/*-------------------------------------.
| yyacceptlab -- YYACCEPT comes here.  |
`-------------------------------------*/
yyacceptlab:
  yyresult = 0;
  goto yyreturn;

/*-----------------------------------.
| yyabortlab -- YYABORT comes here.  |
`-----------------------------------*/
yyabortlab:
  yyresult = 1;
  goto yyreturn;

#if !defined yyoverflow || YYERROR_VERBOSE
/*-------------------------------------------------.
| yyexhaustedlab -- memory exhaustion comes here.  |
`-------------------------------------------------*/
yyexhaustedlab:
  yyerror (YY_("memory exhausted"));
  yyresult = 2;
  /* Fall through.  */
#endif

yyreturn:
  if (yychar != YYEMPTY)
    {
      /* Make sure we have latest lookahead translation.  See comments at
         user semantic actions for why this is necessary.  */
      yytoken = YYTRANSLATE (yychar);
      yydestruct ("Cleanup: discarding lookahead",
                  yytoken, &yylval);
    }
  /* Do not reclaim the symbols of the rule whose action triggered
     this YYABORT or YYACCEPT.  */
  YYPOPSTACK (yylen);
  YY_STACK_PRINT (yyss, yyssp);
  while (yyssp != yyss)
    {
      yydestruct ("Cleanup: popping",
                  yystos[*yyssp], yyvsp);
      YYPOPSTACK (1);
    }
#ifndef yyoverflow
  if (yyss != yyssa)
    YYSTACK_FREE (yyss);
#endif
#if YYERROR_VERBOSE
  if (yymsg != yymsgbuf)
    YYSTACK_FREE (yymsg);
#endif
  return yyresult;
}
