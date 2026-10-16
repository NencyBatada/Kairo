import unicodedata

SEARCH_HEADLINE = 30
SEARCH_HEADLINES = (3, 15)
SEARCH_DESCRIPTION = 90
SEARCH_DESCRIPTIONS = (2, 4)
SEARCH_PATH = 15
KEYWORD = 80
KEYWORD_WORDS = 10
KEYWORDS = (1, 20)
AD_GROUPS = (1, 5)
AD_GROUP_NAME = 100
DEMAND_GEN_HEADLINE = 40
DEMAND_GEN_SHORT_HEADLINE = 30
DEMAND_GEN_HEADLINES = (1, 5)
DEMAND_GEN_LONG_HEADLINE = 90
DEMAND_GEN_LONG_HEADLINES = (1, 5)
DEMAND_GEN_DESCRIPTION = 90
DEMAND_GEN_DESCRIPTIONS = (1, 5)
BUSINESS_NAME = 25
SHOPPING_TITLE = 150
MATCH_TYPES = ("broad", "phrase", "exact")
CALLS_TO_ACTION = (
    "learn_more",
    "shop_now",
    "buy_now",
    "order_now",
    "sign_up",
    "see_more",
    "visit_site",
    "book_now",
    "contact_us",
    "get_quote",
    "download",
    "subscribe",
    "apply_now",
)
DEFAULT_CALL_TO_ACTION = "shop_now"


def length(text: str) -> int:
    return sum(2 if wide(character) else 1 for character in text)


def wide(character: str) -> bool:
    return unicodedata.east_asian_width(character) in ("W", "F")
