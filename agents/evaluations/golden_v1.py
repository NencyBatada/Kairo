def golden() -> dict:
    answers = {}
    for language in ("en", "hi"):
        hindi = language == "hi"
        answers["brief-" + language] = {
            "DraftedSmartBrief": {
                "title": "Rain-ready commutes",
                "objective": "Sell waterproof mesh shoes to commuters in Maharashtra.",
                "kpi": "orders",
                "campaignTypes": ["search", "shopping", "demand_gen"],
                "budget": 200000,
                "startDate": "2026-11-02",
                "endDate": "2026-11-15",
                "regions": ["Maharashtra"],
                "audiences": ["Commuters in Maharashtra"],
                "keyMessages": ["Waterproof mesh for rainy days"],
                "productIds": ["p1"],
                "languages": ["en", "hi"] if hindi else ["en"],
                "reasons": {
                    name: "Supported by the supplied opportunity and product data."
                    for name in (
                        "objective",
                        "kpi",
                        "campaignTypes",
                        "budget",
                        "dates",
                        "regions",
                        "audiences",
                        "keyMessages",
                        "products",
                        "languages",
                    )
                },
            }
        }
        headlines = (
            ["बारिश के लिए जूते", "पैर सूखे रखें", "Acme जूते देखें"]
            if hindi
            else [
                "Waterproof Rain Shoes",
                "Stay Dry On Your Commute",
                "Shop Acme Rain Shoes",
            ]
        )
        descriptions = (
            ["बारिश में रोज़ के सफर के लिए आरामदायक जूते।", "वॉटरप्रूफ जाली वाले Acme जूते देखें।"]
            if hindi
            else [
                "Waterproof mesh shoes for rainy commutes.",
                "Explore Acme rain shoes. Free delivery over ₹999.",
            ]
        )
        answers["copy-" + language] = {
            "DraftSearch": {
                "adGroups": [
                    {
                        "name": "Rain shoes",
                        "productIds": ["p1"],
                        "keywords": [
                            {
                                "text": "बारिश के जूते" if hindi else "waterproof shoes",
                                "match": "phrase",
                            }
                        ],
                        "headlines": headlines,
                        "descriptions": descriptions,
                        "path1": "rain",
                        "path2": "shoes",
                    }
                ]
            },
            "DraftDemandGen": {
                "headlines": [headlines[0]],
                "longHeadlines": [
                    "रोज़ के सफर के लिए वॉटरप्रूफ जूते"
                    if hindi
                    else "Waterproof mesh rain shoes for your daily commute"
                ],
                "descriptions": [descriptions[0]],
                "callToAction": "shop_now",
            },
            "DraftTitles": {
                "titles": [
                    {
                        "productId": "p1",
                        "title": "Acme वॉटरप्रूफ जाली वाले जूते"
                        if hindi
                        else "Acme Waterproof Mesh Rain Shoes",
                    }
                ]
            },
        }
    return answers
