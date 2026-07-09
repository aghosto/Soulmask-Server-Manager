using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using SoulmaskServerManager.Models;

namespace SoulmaskServerManager
{
    public interface ISettingsSubCategory { }

    [JsonConverter(typeof(SoulmaskCoefficientSettingsConverter))]
    public class SoulmaskCoefficientSettings
    {
        public GeneralSettings General { get; set; } = new();
        public ExperienceGrowthSettings ExperienceGrowth { get; set; } = new();
        public OutputDropSettings OutputDrop { get; set; } = new();
        public BuildingSettings Building { get; set; } = new();
        public RefreshSettings Refresh { get; set; } = new();
        public CombatSettings Combat { get; set; } = new();
        public ConsumptionSettings Consumption { get; set; } = new();
        public InvasionSettings Invasion { get; set; } = new();
        public PVPSettingSettings PVP { get; set; } = new();
        public AISettings AI { get; set; } = new();
        public BattleTimeSettings BattleTime { get; set; } = new();
        public ToggleSettings Toggle { get; set; } = new();
        public ServerEventSettings GlobalEvent { get; set; } = new();
    }

    public class SoulmaskCoefficientSettingsConverter : JsonConverter<SoulmaskCoefficientSettings>
    {
        private static readonly Type[] SubCategoryTypes;
        private static readonly Dictionary<string, PropertyInfo> FlatPropertyMap;

        static SoulmaskCoefficientSettingsConverter()
        {
            SubCategoryTypes = typeof(SoulmaskCoefficientSettings).GetProperties()
                .Where(p => typeof(ISettingsSubCategory).IsAssignableFrom(p.PropertyType))
                .Select(p => p.PropertyType)
                .ToArray();

            FlatPropertyMap = new Dictionary<string, PropertyInfo>();
            foreach (var type in SubCategoryTypes)
            {
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.CanRead && prop.CanWrite)
                    {
                        FlatPropertyMap[prop.Name] = prop;
                    }
                }
            }
        }

        public override SoulmaskCoefficientSettings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            var result = new SoulmaskCoefficientSettings();
            var subCatProps = typeof(SoulmaskCoefficientSettings).GetProperties()
                .Where(p => typeof(ISettingsSubCategory).IsAssignableFrom(p.PropertyType))
                .ToDictionary(p => p.Name, p => p);

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    return result;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException();

                string propertyName = reader.GetString();
                reader.Read();

                if (FlatPropertyMap.TryGetValue(propertyName, out var targetProp))
                {
                    var subCat = subCatProps.Values
                        .Select(p => p.GetValue(result))
                        .FirstOrDefault(sc => sc != null && sc.GetType() == targetProp.DeclaringType);

                    if (subCat != null)
                    {
                        object value;
                        if (targetProp.PropertyType == typeof(double))
                            value = reader.GetDouble();
                        else if (targetProp.PropertyType == typeof(int))
                            value = reader.GetInt32();
                        else if (targetProp.PropertyType == typeof(float))
                            value = (float)reader.GetDouble();
                        else
                            value = JsonSerializer.Deserialize(ref reader, targetProp.PropertyType, options);

                        targetProp.SetValue(subCat, value);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                else
                {
                    reader.Skip();
                }
            }

            throw new JsonException("Unexpected end of JSON.");
        }

        public override void Write(Utf8JsonWriter writer, SoulmaskCoefficientSettings value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            var subCatProps = typeof(SoulmaskCoefficientSettings).GetProperties()
                .Where(p => typeof(ISettingsSubCategory).IsAssignableFrom(p.PropertyType));

            foreach (var subCatProp in subCatProps)
            {
                var subCat = subCatProp.GetValue(value) as ISettingsSubCategory;
                if (subCat == null) continue;

                foreach (var prop in subCat.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!prop.CanRead) continue;

                    object propValue = prop.GetValue(subCat);
                    if (prop.PropertyType == typeof(double))
                        writer.WriteNumber(prop.Name, (double)propValue);
                    else if (prop.PropertyType == typeof(int))
                        writer.WriteNumber(prop.Name, (int)propValue);
                    else if (prop.PropertyType == typeof(float))
                        writer.WriteNumber(prop.Name, (float)propValue);
                    else if (prop.PropertyType == typeof(string))
                        writer.WriteString(prop.Name, (string)propValue);
                    else if (prop.PropertyType == typeof(bool))
                        writer.WriteBoolean(prop.Name, (bool)propValue);
                }
            }

            writer.WriteEndObject();
        }
    }

    // 1. 经验与成长
    public class ExperienceGrowthSettings : ISettingsSubCategory
    {
        public double ExpRatio { get; set; } = 1;
        public double CaiJiExpRatio { get; set; } = 1;
        public double ZhiZuoExpRatio { get; set; } = 1;
        public double ShaGuaiExpRatio { get; set; } = 1;
        public double QiTaExpRatio { get; set; } = 1;
        public double ChengZhangExpRatio { get; set; } = 1;
        public double MJExpRatio { get; set; } = 1;
        public double ShuLianDuExpRatio { get; set; } = 1;
        public double TrainingExpRatio { get; set; } = 1;
        public double ShaGuaiExpShareRatio { get; set; } = 0.20000000298023224;
        public double ChuZhanZuRenShaGuaiExpShareRatio { get; set; } = 1;
        public double OtherShaGuaiExpShareRatio { get; set; } = 1;        
        public int BeiDongYiJiShuXingRatio { get; set; } = 1;
        public int ZhuDongYiJiShuXingRatio { get; set; } = 1;
        public double ErJiShuXingRatio { get; set; } = 1;
        public int MaRenBeiDongYiJiShuXingRatio { get; set; } = 1;
        public int MaRenZhuDongYiJiShuXingRatio { get; set; } = 1;
        public double MaRenErJiShuXingRatio { get; set; } = 1;
        public int DongWuBeiDongYiJiShuXingRatio { get; set; } = 1;
        public int DongWuZhuDongYiJiShuXingRatio { get; set; } = 1;
        public double DongWuErJiShuXingRatio { get; set; } = 1;
        public double CurProfInitRatio { get; set; } = 0;
        public int MaxLevel { get; set; } = 60;

    }

    // 2. 产出和掉落
    public class OutputDropSettings : ISettingsSubCategory
    {
        public double CaiJiDiaoLuoRatio { get; set; } = 1;
        public double FaMuDiaoLuoRatio { get; set; } = 1;
        public double CaiKuangDiaoLuoRatio { get; set; } = 1;
        public double DongWuShiTiDiaoLuoRatio { get; set; } = 1;
        public double DongWuShiTiZhongYaoDiaoLuoRatio { get; set; } = 1;
        public double CaiJiShengChanJianZhuDiaoLuoRatio { get; set; } = 1;
        public double PuTongRenDiaoLuoRatio { get; set; } = 1;
        public double JingYingRenDiaoLuoRatio { get; set; } = 1;
        public double BossRenDiaoLuoRatio { get; set; } = 1;
        public double ZuoWuDropRatio { get; set; } = 1;
        public double BaoXiangDropRatio { get; set; } = 1;
        public double CaiJiDamageRatio { get; set; } = 1;
        public double ZiYuanShengMingRatio { get; set; } = 1;
        public double FuHuaSpeed { get; set; } = 1;
        public double ZhiZuoTimeRatio { get; set; } = 1;
        public double ZuoWuShengZhangRatio { get; set; } = 1;
        public double DongWuShengZhangRatio { get; set; } = 1;
        public double FanZhiJianGeRatio { get; set; } = 1;
        public double DongWuShengChanJianGeRatio { get; set; } = 1;
        public double DongWuChanChuRatio { get; set; } = 1;
        public double ZuoWuXiaoHuiRatio { get; set; } = 1;
        public double NormalEquipDropRatioCorrection { get; set; } = 1;
        public double EliteEquipDropRatioCorrection { get; set; } = 1;
        public double BossEquipDropRatioCorrection { get; set; } = 1;
        public double NormalEquipDurabilityCorrection { get; set; } = 1;
        public double EliteEquipDurabilityCorrection { get; set; } = 1;
        public double BossEquipDurabilityCorrection { get; set; } = 1;

    }

    // 3. 建筑
    public class BuildingSettings : ISettingsSubCategory
    {
        public double JianZhuFuLanMul { get; set; } = 1;
        public double JianZhuXiuLiMul { get; set; } = 1;
        public double YingHuoRanShaoSuDuRatio { get; set; } = 1;
        public double NewYingHuoTimeLenMul { get; set; } = 1;
        public double YeShengHitJianZhuShangHaiRatio { get; set; } = 4;
        public double WanJiaHitJianZhuShangHaiRatio { get; set; } = 1;
        public int MaxXiuMianCangCount { get; set; } = 50;
        public int MaxGenRenYingHuoNumber { get; set; } = 6;
        public int MaxGongHuiYingHuoNumber { get; set; } = 6;
        public int MaxChuanSongMenNumber { get; set; } = 10;
        public double MaxPingTaiJianZhuNumMul { get; set; } = 0.10000000149011612;
    }

    // 4. 刷新
    public class RefreshSettings : ISettingsSubCategory
    {
        public double ZhiBeiChongShengRatio { get; set; } = 1;
        public double WanJiaZiYuanJinShuaBanJing { get; set; } = 1;
        public double JianZhuZiYuanJinShuaBanJing { get; set; } = 1;
    }

    // 5. 战斗
    public class CombatSettings : ISettingsSubCategory
    {
        public double DongWuDamageRatio { get; set; } = 1;
        public double DongWuJianShangRatio { get; set; } = 1;
        public double MaRenDamageRatio { get; set; } = 1;
        public double ManRenJianShangRatio { get; set; } = 1;
        public double ManRenTiLiDamageRatio { get; set; } = 1;
        public double ManRenTenacityDamageRatio { get; set; } = 0.5;
        public double ManRenBossTiLiDamageRatio { get; set; } = 1;
        public double ManRenBossTenacityDamageRatio { get; set; } = 0.5;
        public double DongWuTiLiDamageRatio { get; set; } = 1;
        public double DongWuTenacityDamageRatio { get; set; } = 0.5;
        public double DongWuBossTiLiDamageRatio { get; set; } = 1;
        public double DongWuBossTenacityDamageRatio { get; set; } = 0.5;
        public double DamageYeShengRatio { get; set; } = 1;
        public double BeDamageByYeShengRatio { get; set; } = 1;
        public int ReboundDifficulty { get; set; } = 1;
        public double ReleaseControlStatusCDRatio { get; set; } = 1;
        public double RollingInvincibleTimeRatio { get; set; } = 1;
        public double PVP_ShangHaiRatio_WithoutP2P_YouFang { get; set; } = 0;
        public double PVP_ShangHaiRatio_JinZhan { get; set; } = 0.40000000596046448;
        public double PVP_ShangHaiRatio_YuanCheng { get; set; } = 0.40000000596046448;
        public double PVP_ShangHaiRatio_PlayerToPlayer_DiFang { get; set; } = 1;
        public double PVP_GAPVPDamageRatio { get; set; } = 1;
        public double PVP_ShangHaiRatio_PlayerToPlayer_YouFang { get; set; } = 0.059999998658895493;
        public double WanJiaBeiXiaoRenRatio { get; set; } = 0.80000001192092896;
        public double WanJiaBeiXiaoTiRatio { get; set; } = 0.80000001192092896;
        public double DongWuPinZhiRatio { get; set; } = 1;
        public double ManRenPinZhiRatio { get; set; } = 1;
        public double GongJiJianZhuDamageRatio { get; set; } = 1;
        public double ShengMingHuiFuRatio { get; set; } = 1;
        public double TiLiHuiFuRatio { get; set; } = 1;
        public double QiXiHuiFuRatio { get; set; } = 1;
        public double PhysicalRecoveryIntervalRate { get; set; } = 1;
        public double PlayerSweepRangeScale { get; set; } = 1;
        public double JiaSiHuiFuRatio { get; set; } = 1;
    }

    // 6. 消耗
    public class ConsumptionSettings : ISettingsSubCategory
    {
        public double ShiWuXiaoHaoRatio { get; set; } = 1;
        public double ShuiXiaoHaoRatio { get; set; } = 1;
        public double QiXiXiaoHaoRatio { get; set; } = 1;
        public double WuPinFuHuaiRatio { get; set; } = 1;
        public double WuPinXiaoHuiTime { get; set; } = 1;
        public double XiuLiXuYaoCaiLiaoRatio { get; set; } = 1;
        public double XiuLiJiangNaiJiuShangXianRatio { get; set; } = 1;
        public double RanLiaoXiaoHaoRatio { get; set; } = 1;
        public double NaiJiuXiShu { get; set; } = 1;
        public double DongWuXiaoHaoShiWuRatio { get; set; } = 1;
        public double DongWuXiaoHaoShuiRatio { get; set; } = 1;
        public double ZuoWuFeiLiaoXiaoHaoRatio { get; set; } = 1;
        public double ZuoWuShuiXiaoHaoRatio { get; set; } = 1;
    }

    // 7. 入侵
    public class InvasionSettings : ISettingsSubCategory
    {
        public double RuQinGuiMoXiShu { get; set; } = 1;
        public double RuQinQiangDuXiShu { get; set; } = 1;
        public int RuQinGuaiCountMin { get; set; } = 8;
        public int RuQinGuaiCountMax { get; set; } = 128;
        public int RuQinPerBoGuaiMin { get; set; } = 3;
        public int RuQinPerBoGuaiMax { get; set; } = 16;
        public double RuQinGuaiLevelXiShu { get; set; } = 1;
        public int TanChaMinuteLimit { get; set; } = 20;
        public int JinGongMinuteLimit { get; set; } = 90;
        public int LengQueMinuteLimit { get; set; } = 1440;
        public int RuQinMaxChangCiCount { get; set; } = 2;
        public int RuQinBeginHour { get; set; } = 0;
        public int RuQinEndHour { get; set; } = 24;
        public double RuQinShaoChengXiShu { get; set; } = 0.60000002384185791;
        public double RuQinTuShaXiShu { get; set; } = 0.30000001192092896;
        public int RuQinSucceedPrizeTimes { get; set; } = 3;
        public double ManageModeRuQinCountDownTimeRatio { get; set; } = 1;
        public double ReDuXiShu { get; set; } = 1;

    }

    // 8. PVP设置
    public class PVPSettingSettings : ISettingsSubCategory
    {
        public int InitialDefaultAwarenessLevel { get; set; } = 1;
        public int FirstDayMaxAwarenessLevel { get; set; } = 60;
        public int SecondDayMaxAwarenessLevel { get; set; } = 60;
        public int ThirdDayMaxAwarenessLevel { get; set; } = 60;
        public int FourthDayMaxAwarenessLevel { get; set; } = 60;
        public int FifthDayMaxAwarenessLevel { get; set; } = 60;
        public int SixthDayMaxAwarenessLevel { get; set; } = 60;
        public int SeventhDayMaxAwarenessLevel { get; set; } = 60;
        public int EighthDayMaxAwarenessLevel { get; set; } = 60;
        public int NinthDayMaxAwarenessLevel { get; set; } = 60;
        public int TenthDayMaxAwarenessLevel { get; set; } = 60;
        public int PVPTimeAsiaWorkStartTime { get; set; } = 0;
        public int PVPTimeAsiaWorkEndTime { get; set; } = 24;
        public int PVPTimeAsiaNoWorkStartTime { get; set; } = 0;
        public int PVPTimeAsiaNoWorkEndTime { get; set; } = 24;
        public int PVPTimeAmericaWorkStartTime { get; set; } = 0;
        public int PVPTimeAmericaWorkEndTime { get; set; } = 24;
        public int PVPTimeAmericaNoWorkStartTime { get; set; } = 0;
        public int PVPTimeAmericaNoWorkEndTime { get; set; } = 24;
        public int PVPTimeEuropeWorkStartTime { get; set; } = 0;
        public int PVPTimeEuropeWorkEndTime { get; set; } = 24;
        public int PVPTimeEuropeNoWorkStartTime { get; set; } = 0;
        public int PVPTimeEuropeNoWorkEndTime { get; set; } = 24;

    }

    // 9. AI相关
    public class AISettings : ISettingsSubCategory
    {
        public int AIDengJi { get; set; } = 1;
        public int ManRenChuZhanCount { get; set; } = 1;
        public int DongWuChuZhanCount { get; set; } = 1;
    }

    // 10. 战场时间
    public class BattleTimeSettings : ISettingsSubCategory
    {
        public int AsiaWarTimeStart { get; set; } = 10;
        public int AsiaWarTimeEnd { get; set; } = 14;
        public int EuropeWarTimeStart { get; set; } = 17;
        public int EuropeWarTimeEnd { get; set; } = 21;
        public int AmericaWarTimeStart { get; set; } = 0;
        public int AmericaWarTimeEnd { get; set; } = 4;
    }

    // 11. 全服事件
    public class ServerEventSettings : ISettingsSubCategory
    {
        public int SpecialEventGameDist { get; set; } = 0;
        public int SpecialEventAsiaStartTime { get; set; } = 10;
        public int SpecialEventAsiaEndTime { get; set; } = 16;
        public int SpecialEventEuropeStartTime { get; set; } = 17;
        public int SpecialEventEuropeEndTime { get; set; } = 23;
        public int SpecialEventAmericaStartTime { get; set; } = 23;
        public int SpecialEventAmericaEndTime { get; set; } = 5;
        public int SpecialEventTriggerInterval { get; set; } = 3600;
        public int SpecialEventTriggerPercent { get; set; } = 50;
        public int SpecialEventTriggetLimitNum { get; set; } = 2;
        public int SpecialEventServerOpenDay { get; set; } = 1;
    }

    // 13. 开关设置
    public class ToggleSettings : ISettingsSubCategory
    {
        public int BanGlider { get; set; } = 0;
        public int BinSiKaiGuan { get; set; } = 1;
        public int BossDeathEventSwitch { get; set; } = 0;
        public int ChestDropEquipmentMaxQualitySwitch { get; set; } = 0;
        public int DungeonReborn { get; set; } = 1;
        public int DynamicBossStats { get; set; } = 1;
        public int FuHuoMoveSiWangBaoKaiGuan { get; set; } = 0;
        public int HuiFuChuShiBodyData { get; set; } = 1;
        public int IgnoreEnemyJianZhuInSelfYingHuo { get; set; } = 1;
        public int IsOpenGuideTask { get; set; } = 1;
        public int IsPlayBossAppearanceSequence { get; set; } = 1;
        public int JiQiChuZhanKaiGuan { get; set; } = 1;
        public int JianZhuAroundNumLimit { get; set; } = 1;
        public int JianZhuBeDamageLimit { get; set; } = 1;
        public int JianZhuChuanSongMenPlusKaiGuan { get; set; } = 0;
        public int JianZhuFuLanKaiGuan { get; set; } = 1;
        public int JianZhuGaoDuLimit { get; set; } = 1;
        public int JianZhuMirageKaiGuan { get; set; } = 1;
        public int JinJianQuKaiGuan { get; set; } = 1;
        public int JingShenNoXiaoHao { get; set; } = 0;
        public int KaiQiJianZhuHuiXueBuilding { get; set; } = 1;
        public int MakeUseAroundRongQiKaiGuan { get; set; } = 1;
        public int ManageModeRuQin { get; set; } = 0;
        public int MaskRepairUpgradeSwitch { get; set; } = 1;
        public int OpenEscMenuInfJianZao { get; set; } = 0;
        public int PVEOnlyTongGuiShuCanOpenKaiGuan { get; set; } = 1;
        public int PingTaiBuildRangeLimit { get; set; } = 1;
        public int PingTaiJianZhuNumLimit { get; set; } = 1;
        public int PlayerDeathCantDropItemKaiGuan { get; set; } = 0;
        public int ProtectJianZhuInYingHuoSwitch { get; set; } = 0;
        public int RelicChestEventSwitch { get; set; } = 0;
        public int RuQinKaiGuan { get; set; } = 1;
        public int RuinsExplorationKaiGuan { get; set; } = 1;
        public int ShipBlueprintBuildConsumeSwitch { get; set; } = 1;
        public int ShuaXinNPCKaiGuan { get; set; } = 0;
        public int SpecialBossSwitch { get; set; } = 1;
        public int SpecialEventConfigSwitch { get; set; } = 0;
        public int SuiJiRuQinKaiGuan { get; set; } = 1;
        public int SuoDingKaiGuan { get; set; } = 1;
        public double TeShuDaoJuDropXiShuJiaChengKaiGuan { get; set; } = 0;
        public int TransDoorInterworkKaiGuan { get; set; } = 1;
        public int TribalExplorationKaiGuan { get; set; } = 1;
        public int TribalTransportSwitch { get; set; } = 1;
        public int WanMeiChongSu { get; set; } = 1;
        public int WarKaiGuan { get; set; } = 0;
        public int YunXuOtherDaKaiGongZuoTai { get; set; } = 0;
        public int YunXuOtherDaKaiXiangZi { get; set; } = 0;
        public int ZuRenDirectCunQu { get; set; } = 1;
        public int ZuRenFuZhi { get; set; } = 1;
        public int HuDongExcludeBetweenCameraCharacter { get; set; } = 1;
        public int DrawDebugDungeon { get; set; } = 0;
        public int MovementYouHua { get; set; } = 1;
        public int WuLiYouHuaKaiGuan { get; set; } = 1;
        public int BagRepOptimizeSwitch { get; set; } = 1;
        public int RestartGameForceSpawnMonsterSwitch { get; set; } = 1;
        public int PanpaKaiGuan { get; set; } = 1;
        public int KaiQiKuaFu { get; set; } = 0;
        public int JianDuiRuQinKaiGuan { get; set; } = 0;
        public int HuXIangShangHaiKaiGuan { get; set; } = 0;
        public int PlayerYouFangShangHaiKaiGuan { get; set; } = 1;
        public int YouFangShangHaiKaiGuan { get; set; } = 0;
        public int PingTaiAffectNavigation { get; set; } = 1;
    }

    // 12. 通用
    public class GeneralSettings : ISettingsSubCategory
    {
        public double AddRenKeDuRatio { get; set; } = 1;
        public int GongHuiMaxZhaoMuCount { get; set; } = 40;
        public int GeRenMaxZhaoMuCount { get; set; } = 6;
        public int GeRenMaxZhaoMuCount_Two { get; set; } = 10;
        public int GeRenMaxZhaoMuCount_Three { get; set; } = 15;
        public int GongHuiMaxMember { get; set; } = 20;
        public int GeRenBiaoJiMaxCount { get; set; } = 20;
        public int GongHuiBiaoJiMaxCount { get; set; } = 20;
        public int GongHuiMaxDongWuCount { get; set; } = 50;
        public int GeRenMaxDongWuCount { get; set; } = 10;
        public int GongHuiMaxSpecDongWuCount { get; set; } = 1;
        public int GeRenMaxSpecDongWuCount { get; set; } = 1;
        public double ChongsuRatio { get; set; } = 1;
        public double KurmaFuZhongRatio { get; set; } = 1;
        public double MaxFuZhongRatio { get; set; } = 1;
        public int RoleBagCapacity { get; set; } = 60;
        public int XinXiLuRu { get; set; } = 5;
        public double ConverPropsSpeedRatio { get; set; } = 5;
        public int MaxConvertCount { get; set; } = 3;
        public int MainGunUseTimeCD { get; set; } = 1;        
        public double CrewCountRatio { get; set; } = 1;
        public double ZhaoHuanDisRatio { get; set; } = 1;
        public int TiaoWuLengQueTime { get; set; } = 4;
        public double XinQingZengZhang { get; set; } = 1;
        public double XinQingJianShao { get; set; } = 1;
        public int AnimalFollowerMaxCount { get; set; } = 1;
        public double GameWorldDayTimePortion { get; set; } = 0.80000001192092896;
        public int GameWorldTimePower { get; set; } = 24;
        public int BaoXiangDiaoLuoDengJi { get; set; } = 0;
        public int XiuMianOfflineDays { get; set; } = 7;
        public int MaxConveyorCount { get; set; } = 1000;
        public int MaxDongLiKuangChangCount { get; set; } = 10;
        public int GeRenMaxRaftSpaceCount { get; set; } = 2;
        public int GongHuiMaxRaftSpaceCount { get; set; } = 10;
        public int GeRenMaxSpecRaftSpaceCount { get; set; } = 1;
        public int GongHuiMaxSpecRaftSpaceCount { get; set; } = 2;
        public int MaxDiCiCount { get; set; } = 400;
        public double MentalRecoveryRate { get; set; } = 1;
        public int XiuMianDistance { get; set; } = 10000;
        public int HuanXingDistance { get; set; } = 9000;
        public int WuLiYouHuaDist { get; set; } = 6666;
        public int XiShuWeiLing { get; set; } = 0;
    }

    /// <summary>
    /// 系数参数目录：所有游戏系数的显式参数定义，基于 SoulmaskCoefficientSettings 的子类别属性。
    /// 使用 nameof() 确保 GameKey 与属性名的编译时一致性。
    /// </summary>
    public static class SoulmaskCoefficientParameterCatalog
    {
        public static List<ParameterCategory> Categories { get; } = new()
        {
            new ParameterCategory
            {
                ChineseName = "通用",
                EnglishName = "General",
                Params = new List<ParameterDef>
                {
                    new ParameterDef
                    {
                        ChineseName = "驯服蛮人速度倍率",
                        EnglishName = "Barbarian Taming Speed Multiplier",
                        GameKey = nameof(GeneralSettings.AddRenKeDuRatio),
                        Description = "Multiplies the rate at which captured humans gain recognition while being tamed. Higher values = faster speed",
                        Tooltip = "The greater the value, the faster Recognition increases.",
                        Min = 0.1,
                        Max = 1000.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "白天时间占比",
                        EnglishName = "Proportion of daytime",
                        GameKey = nameof(GeneralSettings.GameWorldDayTimePortion),
                        Description = "The speed at which the time of day advances during the day (6:00-18:00) vs at night. At 0.5, the clock speed is the same. Higher values = slower day time and faster night time.",
                        Tooltip = "The greater the value, the shorter the night duration.",
                        Min = 0.0,
                        Max = 1.0,
                        Step = 0.001F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "时间流速倍率",
                        EnglishName = "Time Flow Multiplier",
                        GameKey = nameof(GeneralSettings.GameWorldTimePower),
                        Description = "The number of hours that pass in game per real life hour. Higher values = shorter day cycles.",
                        Tooltip = "The greater the value, the faster the time passes.",
                        Min = 1.0,
                        Max = 288.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "角色重塑时间倍率",
                        EnglishName = "Character Remodel Time Multiplier",
                        GameKey = nameof(GeneralSettings.ChongsuRatio),
                        Description = "Multiplier on the cooldown time after reviving a fallen tribesman. Higher values = longer cooldown time",
                        Tooltip = "The greater the value, the faster the remodeling",
                        Min = 0.1,
                        Max = 100.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "部落招募族人上限",
                        EnglishName = "Tribe Recruitment limit",
                        GameKey = nameof(GeneralSettings.GongHuiMaxZhaoMuCount),
                        Description = "The maximum number of tribesmen that can be recruited by a tribe.",
                        Tooltip = "Tribe Recruitment limit",
                        Min = 1.0,
                        Max = 1000.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "强兼容模块1级增加可招募族人数量",
                        EnglishName = "Lv.1 Strong Compatibility Module increases the number of tribesmen that can be recruited",
                        GameKey = nameof(GeneralSettings.GeRenMaxZhaoMuCount),
                        Description = "When unlocking level 1 of the strong compatibility mask node, the player’s clan size limit is increased by this amount. The resulting limit will be 3 + this setting.",
                        Tooltip = "Lv. 1 Mask Node Connection Enhancement increases max tribesman recruits.",
                        Min = 1.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "强兼容模块2级增加可招募族人数量",
                        EnglishName = "Lv.2 Strong Compatibility Module increases the number of tribesmen that can be recruited",
                        GameKey = nameof(GeneralSettings.GeRenMaxZhaoMuCount_Two),
                        Description = "When unlocking level 2 of the strong compatibility mask node, the player’s clan size limit is increased by this amount. The resulting limit will be 3 + this setting.",
                        Tooltip = "Lv. 2 Mask Node Connection Enhancement increases max tribesman recruits.",
                        Min = 1.0,
                        Max = 200.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "强兼容模块3级增加可招募族人数量",
                        EnglishName = "Lv.3 Strong Compatibility Module increases the number of tribesmen that can be recruited",
                        GameKey = nameof(GeneralSettings.GeRenMaxZhaoMuCount_Three),
                        Description = "When unlocking level 3 of the strong compatibility mask node, the player’s clan size limit is increased by this amount. The resulting limit will be 3 + this setting.",
                        Tooltip = "Lv. 3 Mask Node Connection Enhancement increases max tribesman recruits.",
                        Min = 1.0,
                        Max = 1000.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "心情增长倍率",
                        EnglishName = "Mood Increase Multiplier",
                        GameKey = nameof(GeneralSettings.XinQingZengZhang),
                        Description = "Whenever a tribesman’s mood increases, multiply the amount gained by this value.",
                        Tooltip = "The higher the value, the higher the Mood increase rate when tribesmen's needs are met.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "心情减少倍率",
                        EnglishName = "Mood Reduction Multiplier",
                        GameKey = nameof(GeneralSettings.XinQingJianShao),
                        Description = "Whenever a tribesman’s mood decreases, multiply the amount lost by this value.",
                        Tooltip = "The higher the value, the higher the Mood reduction rate when tribesmen's needs are unmet.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "部落动物数量上限",
                        EnglishName = "Tribe Animals Quantity Limit",
                        GameKey = nameof(GeneralSettings.GongHuiMaxDongWuCount),
                        Description = "The maximum number of tamed animals that can be in a tribe.",
                        Tooltip = "Tribe Animals Quantity Limit",
                        Min = 1.0,
                        Max = 1000.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "个人动物数量上限",
                        EnglishName = "Personal Animal Quantity Limit",
                        GameKey = nameof(GeneralSettings.GeRenMaxDongWuCount),
                        Description = "The maximum number of tamed animals that can be owned by a single player.",
                        Tooltip = "Personal Animal Quantity Limit",
                        Min = 1.0,
                        Max = 1000.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "宝箱掉落包等级",
                        EnglishName = "Chest Drop Level",
                        GameKey = nameof(GeneralSettings.BaoXiangDiaoLuoDengJi),
                        Min = 0.0,
                        Max = 4.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "玩家离线多少天后族人休眠不工作",
                        EnglishName = "How many days would clanmates hibernate without working after the player goes offline?",
                        GameKey = nameof(GeneralSettings.XiuMianOfflineDays),
                        Description = "After a player logs out of a game, their clanmates will continue working their assigned jobs as long as the game is still being hosted. If the player remains offline for this number of real life days, their clanmates will go into hibernation - meaning they will just sit there and do nothing - until the player logs back into the game.",
                        Tooltip = "How many days of being offline will result in your tribesmen no longer working?",
                        Min = 0.0,
                        Max = 360.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "跳舞冷却时间(h)",
                        EnglishName = "Dancing cooldown Time (h)",
                        GameKey = nameof(GeneralSettings.TiaoWuLengQueTime),
                        Description = "After a tribesman performs a dance at a bonfire, it will trigger a cooldown before they are allowed to perform another dance. This setting determines that cooldown, measured in real life hours.",
                        Tooltip = "Dancing Interval (in hours)",
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "信息录入数量上限",
                        EnglishName = "Info Entry Limit",
                        GameKey = nameof(GeneralSettings.XinXiLuRu),
                        Description = "Determines the maximum number of tribesman per player that can be stored and later resurrected.",
                        Tooltip = "Max number of tribesmen info allowed for Info Entry",
                        Min = 1.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "最大负重倍率（服务器重启生效）",
                        EnglishName = "Max Load Multiplier (Effective upon server restart)",
                        GameKey = nameof(GeneralSettings.MaxFuZhongRatio),
                        Description = "Multiplies the maximum carry capacity by this value. (Need to test whether it affects npcs or only players.)",
                        Tooltip = "Max Load Multiplier",
                        Min = 1.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "角色包裹容量（服务器重启或角色重生生效）",
                        EnglishName = "Character Backpack Capacity (Effective upon server restart or character revival)",
                        GameKey = nameof(GeneralSettings.RoleBagCapacity),
                        Description = "Sets the maximum number of inventory slots for players. (Need to test whether it also affects npcs.)",
                        Tooltip = "Default Character Backpack Capacity Settings",
                        Min = 30.0,
                        Max = 256.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "个人标记个数上限",
                        EnglishName = "Max Number of Personal Marks",
                        GameKey = nameof(GeneralSettings.GeRenBiaoJiMaxCount),
                        Description = "The maximum number of custom map markers that can be placed by a single player.",
                        Min = 1.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "共享标记个数上限",
                        EnglishName = "Max Number of Shared Marks",
                        GameKey = nameof(GeneralSettings.GongHuiBiaoJiMaxCount),
                        Description = "The maximum number of custom map markers that can be shared with a tribe.",
                        Min = 1.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "部落人数",
                        EnglishName = "Tribe Persons",
                        GameKey = nameof(GeneralSettings.GongHuiMaxMember),
                        Description = "The maximum number of players that can join a tribe. This can also be set on the command line when starting the server. If it is set, then the value will revert to the command line value on server restart.",
                        Tooltip = "Tribe Players",
                        Min = 1.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "召唤距离系数",
                        EnglishName = "Summoning Distance Coefficient",
                        GameKey = nameof(GeneralSettings.ZhaoHuanDisRatio),
                        Description = "Multiplies the distance at which you can summon a deployed mount or NPC to your location. At a value of 1, the distance is 100 meters. At 2, the distance is 200 meters. At 100, the distance is 10 kilometers, which should cover the entire map.",
                        Tooltip = "Mount summon distance. The higher the value, the farther you can summon a mount.",
                        Min = 1.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "神秘转化台转化效率",
                        EnglishName = "Mysterious Converter Conversion Rate",
                        GameKey = nameof(GeneralSettings.ConverPropsSpeedRatio),
                        Description = "Multiplier for the conversion rate of the mysterious converter. Higher values will convert more items at once.",
                        Tooltip = "Greater the value, faster the conversion with the Mysterious Converter.",
                        Min = 1.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "神秘转化台建造上限",
                        EnglishName = "Max Mysterious Converter Constructions",
                        GameKey = nameof(GeneralSettings.MaxConvertCount),
                        Description = "Maximum number of mysterious converters that can be built by a tribe.",
                        Tooltip = "Max Mysterious Converters Constructible by Individual or Tribe",
                        Min = 1.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "巨象与船只负重物品重量倍率（值越小能负载的物品越多）",
                        EnglishName = "Giant Elephant and Boat Load Weight Multiplier (lower values allow more items to be carried)",
                        GameKey = nameof(GeneralSettings.KurmaFuZhongRatio),
                        Tooltip = "The smaller the value, the higher the weight capacity of platform animals.",
                        Min = 0.0,
                        Max = 1.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "部落特定动物（巨象）数量上限",
                        EnglishName = "Tribe Specific Animal (Giant Elephant) Max Quantity",
                        GameKey = nameof(GeneralSettings.GongHuiMaxSpecDongWuCount),
                        Description = "Maximum number of giant elephants that can be owned by a tribe",
                        Min = 1.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "个人特定动物（巨象）数量上限",
                        EnglishName = "Personal Specific Animal (Giant Elephant) Max Quantity",
                        GameKey = nameof(GeneralSettings.GeRenMaxSpecDongWuCount),
                        Description = "Maximum number of giant elephants that can be owned by a player",
                        Min = 1.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "精神恢复速度系数",
                        EnglishName = "Morale Recovery Speed Coefficient",
                        GameKey = nameof(GeneralSettings.MentalRecoveryRate),
                        Description = "Morale Recovery Speed.",
                        Tooltip = "Morale Recovery Speed. The higher the value, the faster the morale recovers.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "可跟随的动物的最大数量",
                        EnglishName = "Max followable animals",
                        GameKey = nameof(GeneralSettings.AnimalFollowerMaxCount),
                        Description = "Maximum number of animals that can follow a player at once.",
                        Tooltip = "Max followable animals",
                        Min = 0.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "动力索道建筑建造上限",
                        EnglishName = "Power Ropeway Construction Limit",
                        GameKey = nameof(GeneralSettings.MaxConveyorCount),
                        Description = "Max Number of Power Ropeways Buildable per Player or Tribe",
                        Tooltip = "Max Number of Power Ropeways Buildable per Player or Tribe",
                        Min = 1.0,
                        Max = 10000.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "动力矿场建造上限",
                        EnglishName = "Max Power Mine Constructions",
                        GameKey = nameof(GeneralSettings.MaxDongLiKuangChangCount),
                        Description = "Max Number of Power Mines Buildable per Player or Tribe",
                        Tooltip = "Max Number of Power Mines Buildable per Player or Tribe",
                        Min = 1.0,
                        Max = 1000.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "个人船只数量",
                        EnglishName = "Personal Ship Quantity",
                        GameKey = nameof(GeneralSettings.GeRenMaxRaftSpaceCount),
                        Min = 1.0,
                        Max = 50.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "部落船只数量",
                        EnglishName = "Tribe Ship Quantity",
                        GameKey = nameof(GeneralSettings.GongHuiMaxRaftSpaceCount),
                        Min = 1.0,
                        Max = 50.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "个人鳐级飞行舰数量",
                        EnglishName = "Personal Shark-class Airship Quantity",
                        GameKey = nameof(GeneralSettings.GeRenMaxSpecRaftSpaceCount),
                        Min = 1.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "部落鳐级飞行舰数量",
                        EnglishName = "Tribal Shark-class Airship Quantity",
                        GameKey = nameof(GeneralSettings.GongHuiMaxSpecRaftSpaceCount),
                        Min = 1.0,
                        Max = 20.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "地刺最大数量",
                        EnglishName = "Max Groundspikes",
                        GameKey = nameof(GeneralSettings.MaxDiCiCount),
                        Min = 1.0,
                        Max = 1000.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "船员数量倍率",
                        EnglishName = "Crew Count Multiplier",
                        GameKey = nameof(GeneralSettings.CrewCountRatio),
                        Tooltip = "The higher the value, the greater the number of assignable crew members.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "鳐级飞行舰主炮使用时间间隔",
                        EnglishName = "Shark-class Airship Main Cannon Cooldown",
                        GameKey = nameof(GeneralSettings.MainGunUseTimeCD),
                        Description = "Shark-class Airship Main Cannon Cooldown (Minutes)",
                        Tooltip = "Shark-class Airship Main Cannon Cooldown (Minutes)",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "休眠距离",
                        EnglishName = "Hibernation Countdown",
                        GameKey = nameof(GeneralSettings.XiuMianDistance),
                        Description = "The distance around all connected players beyond which AI controlled entities will stop processing and freeze in place. Distance is measured in centimeters. Should always be higher than Awakening Distance to prevent bugs.",
                        Tooltip = "",
                        Min = 3000.0,
                        Max = 16000.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "唤醒距离",
                        EnglishName = "Awakening Distance",
                        GameKey = nameof(GeneralSettings.HuanXingDistance),
                        Description = "The distance around all connected players within which AI controlled entities will leave hibernation and become active. Distance is measured in centimeters. Should always be lower than Hibernation Distance to prevent bugs.",
                        Tooltip = "",
                        Min = 2000.0,
                        Max = 15100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "物理优化距离",
                        EnglishName = "Physical Optimized Distance",
                        GameKey = nameof(GeneralSettings.WuLiYouHuaDist),
                        Description = "This is believed to be the distance around all players outside of which physics simulation will run at a reduced capacity. Distance is measured in centimeters.",
                        Tooltip = "",
                        Min = 5000.0,
                        Max = 9000.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "系数为0",
                        EnglishName = "Coefficient Zero",
                        GameKey = nameof(GeneralSettings.XiShuWeiLing),
                        Tooltip = "",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                        IsEnabled = false,
                    },
                }
            },
            new ParameterCategory
            {
                ChineseName = "经验与成长",
                EnglishName = "Exp & Growth",
                Params = new List<ParameterDef>
                {
                    new ParameterDef
                    {
                        ChineseName = "意识强度经验倍率",
                        EnglishName = "Awareness Strength EXP Multiplier",
                        GameKey = nameof(ExperienceGrowthSettings.ExpRatio),
                        Description = "Whenever a player gains awareness experience, it will be multiplied by this value. Awareness experience is used to unlock new technologies.",
                        Tooltip = "The greater the value, the faster the Awareness Strength increases.",
                        Min = 0.1,
                        Max = 100.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "角色经验倍率",
                        EnglishName = "Character EXP Multiplier",
                        GameKey = nameof(ExperienceGrowthSettings.ChengZhangExpRatio),
                        Description = "Whenever a player character or tribesman gains experience, it will be multiplied by this value.",
                        Tooltip = "The greater the value, the more Body EXP a character gains.",
                        Min = 0.1,
                        Max = 100.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "面甲经验倍率",
                        EnglishName = "Mask EXP Multiplier",
                        GameKey = nameof(ExperienceGrowthSettings.MJExpRatio),
                        Description = "Whenever a player gains mask experience, it will be multiplied by this value.",
                        Tooltip = "The greater the value, the faster Mask EXP is gained.",
                        Min = 0.1,
                        Max = 100.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "熟练度经验倍率",
                        EnglishName = "Proficiency EXP Multiplier",
                        GameKey = nameof(ExperienceGrowthSettings.ShuLianDuExpRatio),
                        Description = "Whenever a player character or tribesman gains proficiency experience, it will be multiplied by this value. Proficiency affects a character’s skill with tools, weapons and professions and is gained by practicing with those tools, weapons and professions.",
                        Tooltip = "The greater the value, the more Body Proficiency EXP.",
                        Min = 0.1,
                        Max = 100.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "采集经验倍率",
                        EnglishName = "Collection EXP Multiplier",
                        GameKey = nameof(ExperienceGrowthSettings.CaiJiExpRatio),
                        Description = "Whenever a player character or tribesman gains experience from collecting resources, it will be multiplied by this value.",
                        Tooltip = "The greater the value, the greater the EXP when harvesting, logging, and mining.",
                        Min = 0.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "制作经验倍率",
                        EnglishName = "Craft EXP Multiplier",
                        GameKey = nameof(ExperienceGrowthSettings.ZhiZuoExpRatio),
                        Description = "Whenever a player character or tribesman gains experience from crafting, it will be multiplied by this value.",
                        Tooltip = "The greater the value, the more Craft EXP is gained.",
                        Min = 0.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "杀怪经验倍率",
                        EnglishName = "Monster-killing EXP Multiplier",
                        GameKey = nameof(ExperienceGrowthSettings.ShaGuaiExpRatio),
                        Description = "Whenever a player character or tribesman gains experience from killing enemy npcs and animals, it will be multiplied by this value.",
                        Tooltip = "The greater the value, the more EXP for killing enemies, animals, or machines.",
                        Min = 0.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "其他经验倍率",
                        EnglishName = "Other EXP Multiplier",
                        GameKey = nameof(ExperienceGrowthSettings.QiTaExpRatio),
                        Description = "Multiplier on experience gains from sources not covered by other experience multipliers.",
                        Tooltip = "The greater the value, the greater the EXP multiplier for other actions.",
                        Min = 0.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "角色升级获得默认属性点数倍率",
                        EnglishName = "Multiplier of Stat Points obtained by default when upgrading characters",
                        GameKey = nameof(ExperienceGrowthSettings.BeiDongYiJiShuXingRatio),
                        Description = "Whenever a starting player character levels up and their stats are increased, the amount of the increase will be multiplied by this value. Changes to this setting only affect newly gained levels.",
                        Tooltip = "Effective only against starting characters.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "角色升级获得可用属性点数倍率",
                        EnglishName = "Multiplier of available Stat Points obtained when upgrading characters",
                        GameKey = nameof(ExperienceGrowthSettings.ZhuDongYiJiShuXingRatio),
                        Description = "Whenever a starting player character levels up and gains points to spend on stats, the number of points gained will be multiplied by this value. Changes to this setting only affect newly gained levels.",
                        Tooltip = "Effective only against starting characters.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "角色升级获得血攻防提升的倍率",
                        EnglishName = "Multiplier of HP, ATK and DEF boosts when upgrading characters",
                        GameKey = nameof(ExperienceGrowthSettings.ErJiShuXingRatio),
                        Description = "Whenever a starting player character levels up and gains additional HP, ATK and DEF stats, the amount gained will be multiplied by this number. Changes to this setting only affect newly gained levels.",
                        Tooltip = "Effective only against starting characters.",
                        Min = 0.5,
                        Max = 10.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "蛮人升级获得默认属性点数倍率",
                        EnglishName = "Multiplier of Stat Points obtained by default when upgrading barbarians",
                        GameKey = nameof(ExperienceGrowthSettings.MaRenBeiDongYiJiShuXingRatio),
                        Description = "Whenever a tribesman levels up and their stats are increased, the amount of the increase will be multiplied by this value. Changes to this setting only affect newly gained levels. Whevever a human NPC spawns in the world, this setting will affect their stats as if they had leveled from 1 to their spawned level. Changes to this setting only affect newly spawned NPCs.",
                        Tooltip = "Applies to all Barbarians. Note: This includes wild barbarians.",
                        Min = 1.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "蛮人升级获得可用属性点数倍率",
                        EnglishName = "Multiplier of available Stat Points obtained when upgrading barbarians",
                        GameKey = nameof(ExperienceGrowthSettings.MaRenZhuDongYiJiShuXingRatio),
                        Description = "Whenever a tribesman levels up and gains points to spend on stats, the number of points gained will be multiplied by this value. Changes to this setting only affect newly gained levels. Whevever a human NPC spawns in the world, this setting will affect their stats as if they had leveled from 1 to their spawned level and spent their stat points. Changes to this setting only affect newly spawned NPCs.",
                        Tooltip = "Applies to all Barbarians. Note: This includes wild barbarians.",
                        Min = 1.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "蛮人升级获得血攻防提升的倍率",
                        EnglishName = "Multiplier of HP, ATK and DEF boosts when upgrading barbarians",
                        GameKey = nameof(ExperienceGrowthSettings.MaRenErJiShuXingRatio),
                        Description = "Whenever a tribesman levels up and gains additional HP, ATK and DEF stats, the amount gained will be multiplied by this number. Changes to this setting only affect newly gained levels. Whevever a human NPC spawns in the world, this setting will affect their stats as if they had leveled from 1 to their spawned level. Changes to this setting only affect newly spawned NPCs.",
                        Tooltip = "Applies to all Barbarians. Note: This includes wild barbarians.",
                        Min = 0.5,
                        Max = 10.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "动物升级获得默认属性点数倍率",
                        EnglishName = "Multiplier of Stat Points obtained by default when upgrading animals",
                        GameKey = nameof(ExperienceGrowthSettings.DongWuBeiDongYiJiShuXingRatio),
                        Description = "Whevever an animal or mechanical NPC spawns in the world, this setting will multiply their gained stat points as if they had leveled from 1 to their spawned level. Changes to this setting only affect newly spawned NPCs.",
                        Tooltip = "Effective against all wild animals and machines.",
                        Min = 1.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "动物升级获得可用属性点数倍率",
                        EnglishName = "Multiplier of available Stat Points obtained when upgrading animals",
                        GameKey = nameof(ExperienceGrowthSettings.DongWuZhuDongYiJiShuXingRatio),
                        Description = "Whevever an animal or mechanical NPC spawns in the world, this setting will multiply their gained spare stat points as if they had leveled from 1 to their spawned level and spent their points. Changes to this setting only affect newly spawned NPCs.",
                        Tooltip = "Effective against all wild animals and machines.",
                        Min = 1.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "动物升级获得血攻防提升的倍率",
                        EnglishName = "Multiplier of HP, ATK and DEF boosts when upgrading animals",
                        GameKey = nameof(ExperienceGrowthSettings.DongWuErJiShuXingRatio),
                        Description = "Whevever an animal or mechanical NPC spawns in the world, this setting will multiply their gained HP, ATK and DEF stats as if they had leveled from 1 to their spawned level. Changes to this setting only affect newly spawned NPCs.",
                        Tooltip = "Effective against all wild animals and machines.",
                        Min = 0.5,
                        Max = 10.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "击杀经验共享系数",
                        EnglishName = "Kill EXP Sharing Coefficient",
                        GameKey = nameof(ExperienceGrowthSettings.ShaGuaiExpShareRatio),
                        Description = "When a player gains experience from killing enemies, a portion of that experience will also be gained by nearby players within the same tribe. The amount of experience gained by the other players will be the amount gained by the player who killed the enemy multiplied by this value.",
                        Tooltip = "The higher the value, the more the shared kill EXP.",
                        Min = 0.0,
                        Max = 1.0,
                        Step = 0.001F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "最大意识等级",
                        EnglishName = "Max Awareness Strength",
                        GameKey = nameof(ExperienceGrowthSettings.MaxLevel),
                        Description = "The maximum awareness level that can be reached by players. Reducing this will reduce the maximum obtainable technology.",
                        Tooltip = "Max Awareness Strength Level",
                        Min = 1.0,
                        Max = 60.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "训练场经验倍率",
                        EnglishName = "Training Ground EXP Multiplier",
                        GameKey = nameof(ExperienceGrowthSettings.TrainingExpRatio),
                        Tooltip = "The higher the value, the higher the training efficiency of Training Ground.",
                        Min = 0.1,
                        Max = 100.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "出战族人杀怪经验共享系数",
                        EnglishName = "Monster-killing EXP Sharing Coefficient for deployed tribesmen",
                        GameKey = nameof(ExperienceGrowthSettings.ChuZhanZuRenShaGuaiExpShareRatio),
                        Tooltip = "The higher the value, the more the shared monster-killing EXP for deployed tribesmen.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "出战坐骑、跟随非人形对象杀怪经验共享系数",
                        EnglishName = "Monster-killing EXP Sharing Coefficient for deployed mounts and non-humanoid followers",
                        GameKey = nameof(ExperienceGrowthSettings.OtherShaGuaiExpShareRatio),
                        Tooltip = "The higher the value, the more the shared monster-killing EXP for deployed mounts and non-humanoid followers.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "精英或Boss的当前熟练度等级进度系数",
                        EnglishName = "Current Proficiency Progress Coefficient for Elites/Bosses",
                        GameKey = nameof(ExperienceGrowthSettings.CurProfInitRatio),
                        Tooltip = "Coefficient 0: Initial proficiency level. Coefficient 1: Proficiency level at maximum capacity. Intermediate value: Between initial and maximum levels.",
                        Min = 0.0,
                        Max = 1.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                }
            },
            new ParameterCategory
            {
                ChineseName = "产出与掉落",
                EnglishName = "Output & Drops",
                Params = new List<ParameterDef>
                {
                    new ParameterDef
                    {
                        ChineseName = "采集产出倍率",
                        EnglishName = "Collecting Output Multiplier",
                        GameKey = nameof(OutputDropSettings.CaiJiDiaoLuoRatio),
                        Description = "Multiplies resources gained per hit when collecting with hands or scythe.",
                        Tooltip = "The greater the value, the higher the yield from collection each time.",
                        Min = 1.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "伐木产出倍率",
                        EnglishName = "Logging Drop Multiplier",
                        GameKey = nameof(OutputDropSettings.FaMuDiaoLuoRatio),
                        Description = "Multiplies resources gained per hit when logging.",
                        Tooltip = "The greater the value, the higher the yield from logging each time.",
                        Min = 1.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "采矿产出倍率",
                        EnglishName = "Mining Output Multiplier",
                        GameKey = nameof(OutputDropSettings.CaiKuangDiaoLuoRatio),
                        Description = "Multiplies resources gained per hit when mining.",
                        Tooltip = "The greater the value, the higher the yield from mining each time.",
                        Min = 1.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "屠宰产出倍率",
                        EnglishName = "Slaughtering Output Multiplier",
                        GameKey = nameof(OutputDropSettings.DongWuShiTiDiaoLuoRatio),
                        Description = "Multiplies resources gained when harvesting an animal.",
                        Tooltip = "The greater the value, the higher the yield from dissecting animal carcasses.",
                        Min = 1.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "动物部位产出倍率",
                        EnglishName = "Animal Part Output Multiplier",
                        GameKey = nameof(OutputDropSettings.DongWuShiTiZhongYaoDiaoLuoRatio),
                        Description = "Multiplies special resources gained when harvesting an animal.",
                        Tooltip = "The greater the value, the more the extra drops from special animal carcasses (e.g., Saber-toothed Predator Fangs, Sobek Crocodile Tails).",
                        Min = 1.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "自动化生产基地产出倍率",
                        EnglishName = "Automated Production Base's Output Multiplier",
                        GameKey = nameof(OutputDropSettings.CaiJiShengChanJianZhuDiaoLuoRatio),
                        Tooltip = "The greater the value, the higher the output of automated production bases.",
                        Min = 0.5,
                        Max = 10.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "普通蛮人掉落倍率",
                        EnglishName = "Normal Barbarian Drop Multiplier",
                        GameKey = nameof(OutputDropSettings.PuTongRenDiaoLuoRatio),
                        Description = "Multiplier applied to the amount of items dropped by normal wild humans when killed. Does not affect dropped gear.",
                        Tooltip = "Normal Drop Multiplier (Barbarian weapons/gear not affected)",
                        Min = 1.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "精英蛮人掉落倍率",
                        EnglishName = "Elite Barbarian Drop Multiplier",
                        GameKey = nameof(OutputDropSettings.JingYingRenDiaoLuoRatio),
                        Description = "Multiplier applied to the amount of items dropped by elite wild humans when killed. Does not affect dropped gear.",
                        Tooltip = "Elite Monster Drop Multiplier (Elite Barbarian weapons/gear not affected)",
                        Min = 1.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "Boss掉落倍率",
                        EnglishName = "Boss Drop Multiplier",
                        GameKey = nameof(OutputDropSettings.BossRenDiaoLuoRatio),
                        Description = "Multiplier applied to the amount of items dropped by bosses when killed. Does not affect dropped gear.",
                        Tooltip = "Multiplier for boss drops (Barbarian weapons/gear not affected).",
                        Min = 1.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "种植作物产出倍率",
                        EnglishName = "Plant Crop Yield Multiplier",
                        GameKey = nameof(OutputDropSettings.ZuoWuDropRatio),
                        Description = "Multiplies the amount gained when harvesting crops.",
                        Tooltip = "The greater the value, the higher the yield per crop harvest.",
                        Min = 0.5,
                        Max = 10.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "作物生长速度倍率",
                        EnglishName = "Crop Growth Speed Multiplier",
                        GameKey = nameof(OutputDropSettings.ZuoWuShengZhangRatio),
                        Description = "Multiplies the speed at which crops grow. Higher values = shorter growth times.",
                        Tooltip = "The greater the value, the faster the crops grow.",
                        Min = 0.1,
                        Max = 1000.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "制作速度倍率",
                        EnglishName = "Crafting Speed Multiplier",
                        GameKey = nameof(OutputDropSettings.ZhiZuoTimeRatio),
                        Description = "Multiplies the speed for completing crafting tasks. Higher values = shorter crafting times",
                        Tooltip = "The greater the value, the faster the crafting speed.",
                        Min = 0.1,
                        Max = 100.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "宝箱掉落倍率",
                        EnglishName = "Chest Drop Multiplier",
                        GameKey = nameof(OutputDropSettings.BaoXiangDropRatio),
                        Description = "Multiplies the amount of items found in loot chests.",
                        Tooltip = "The greater the value, the more the supplies from various chests.",
                        Min = 1.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "采集效率倍率",
                        EnglishName = "Collecting Efficiency Multiplier",
                        GameKey = nameof(OutputDropSettings.CaiJiDamageRatio),
                        Description = "Multiplies how much damage you do to resource collection objects, affecting resources gained per hit and total hits per object.",
                        Tooltip = "The greater the value, the greater the efficiency when collecting.",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "采集资源生命系数",
                        EnglishName = "Resource Collection HP Coefficient",
                        GameKey = nameof(OutputDropSettings.ZiYuanShengMingRatio),
                        Description = "Multiplies the amount of HP resource collection objects have, which affects the total resource yield of the objects.",
                        Tooltip = "The greater the value, the more opportunities to collect resources.",
                        Min = 0.1,
                        Max = 10.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "动物生长速度",
                        EnglishName = "Animal Growth Speed",
                        GameKey = nameof(OutputDropSettings.DongWuShengZhangRatio),
                        Description = "Multiplies the growth speed of captured baby animals. Higher values = shorter time to reach adulthood",
                        Tooltip = "The higher the value, the faster the growth speed of bred animals.",
                        Min = 0.0,
                        Max = 1000.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "动物繁殖效率",
                        EnglishName = "Animal Reproduction Efficiency",
                        GameKey = nameof(OutputDropSettings.FanZhiJianGeRatio),
                        Description = "Multiplies the rate at which tamed animals reproduce.",
                        Tooltip = "The higher the value, the faster the reproduction speed of bred animals.",
                        Min = 0.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "动物生产效率",
                        EnglishName = "Animal Production Efficiency",
                        GameKey = nameof(OutputDropSettings.DongWuShengChanJianGeRatio),
                        Description = "Multiplies the amount of resources produced by tamed animals.",
                        Tooltip = "The higher the value, the higher the production efficiency of bred animals.",
                        Min = 0.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "动物产出倍率",
                        EnglishName = "Animal Yield Multiplier",
                        GameKey = nameof(OutputDropSettings.DongWuChanChuRatio),
                        Description = "Multiplies the amount of resources obtained from slaughtering tamed animals.",
                        Tooltip = "The higher the value, the higher the yield of bred animals.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "作物销毁速度",
                        EnglishName = "Crop Destruction Speed",
                        GameKey = nameof(OutputDropSettings.ZuoWuXiaoHuiRatio),
                        Description = "Multiplies the speed at which crops will be destroyed if left unharvested. Higher values = shorter time",
                        Tooltip = "The higher the value, the higher the crop destruction speed.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "孵化速度",
                        EnglishName = "Hatching Speed",
                        GameKey = nameof(OutputDropSettings.FuHuaSpeed),
                        Description = "Multiplies the incubation time for eggs. Higher values = less time to hatch",
                        Tooltip = "The greater the value, the faster the incubation.",
                        Min = 0.0,
                        Max = 1000.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "普通蛮人武器装备掉落倍率",
                        EnglishName = "Normal Barbarian Gear Drop Rate Modifier",
                        GameKey = nameof(OutputDropSettings.NormalEquipDropRatioCorrection),
                        Tooltip = "Multiplier for normal barbarian gear drops (In PVP mode, only affects advanced weapons/armor exceeding the current server level limit)",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "精英蛮人武器装备掉落倍率",
                        EnglishName = "Elite Gear Drop Rate Modifier",
                        GameKey = nameof(OutputDropSettings.EliteEquipDropRatioCorrection),
                        Tooltip = "Multiplier for barbarian elite gear drops (In PVP mode, only affects advanced weapons/armor exceeding the current server level limit)",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "Boss武器装备掉落倍率",
                        EnglishName = "Boss Gear Drop Rate Modifier",
                        GameKey = nameof(OutputDropSettings.BossEquipDropRatioCorrection),
                        Tooltip = "Multiplier for barbarian boss gear drops (In PVP mode, only affects advanced weapons/armor exceeding the current server level limit)",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "普通蛮人武器装备掉落耐久度",
                        EnglishName = "Normal Barbarian Dropped Gear Durability Modifier",
                        GameKey = nameof(OutputDropSettings.NormalEquipDurabilityCorrection),
                        Tooltip = "Common Barbarian gear initial durability ratio multiplier (Max 100% durability; In PVP mode, only affects advanced weapons/armor exceeding the current server level limit)",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "精英蛮人武器装备掉落耐久度",
                        EnglishName = "Elite Dropped Gear Durability Modifier",
                        GameKey = nameof(OutputDropSettings.EliteEquipDurabilityCorrection),
                        Tooltip = "Barbarian Elite gear initial durability ratio multiplier (Max 100% durability; In PVP mode, only affects advanced weapons/armor exceeding the current server level limit)",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "Boss武器装备掉落耐久度",
                        EnglishName = "Boss Dropped Gear Durability Modifier",
                        GameKey = nameof(OutputDropSettings.BossEquipDurabilityCorrection),
                        Tooltip = "Barbarian Boss gear initial durability ratio multiplier (Max 100% durability; In PVP mode, only affects advanced weapons/armor exceeding the current server level limit)",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                }
            },
            new ParameterCategory
            {
                ChineseName = "建筑",
                EnglishName = "Building",
                Params = new List<ParameterDef>
                {
                    new ParameterDef
                    {
                        ChineseName = "建筑腐烂系数",
                        EnglishName = "Building Decay Coefficient",
                        GameKey = nameof(BuildingSettings.JianZhuFuLanMul),
                        Description = "Multiplies the rate at which player buildings will decay if not protected by a bonfire. Higher values = shorter decay time",
                        Tooltip = "The greater the value, the quicker a structure decays.",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "建筑建造与修理速度系数",
                        EnglishName = "Multiplier of building construction and repair speed",
                        GameKey = nameof(BuildingSettings.JianZhuXiuLiMul),
                        Description = "Multiplies the rate at which buildings will regenerate HP after being constructed or repaired. Higher values = shorter time to heal",
                        Tooltip = "The greater the value, the faster the HP increases when building or repairing a structure.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "野生对象对建筑伤害倍率",
                        EnglishName = "DMG Multiplier of wild objects against buildings",
                        GameKey = nameof(BuildingSettings.YeShengHitJianZhuShangHaiRatio),
                        Description = "Multiplies the damage done to player buildings by wild (not player owned) npcs and animals",
                        Tooltip = "The higher the value, the greater the damage dealt by wild monsters to buildings.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "玩家归属对象对建筑伤害倍率",
                        EnglishName = "DMG Multiplier of player's affiliated objects against buildings",
                        GameKey = nameof(BuildingSettings.WanJiaHitJianZhuShangHaiRatio),
                        Description = "Multiplies the damage dealt by players and player owned units against player buildings.",
                        Tooltip = "The higher the value, the higher the player faction's damage multiplier to buildings.",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "营火燃烧速度系数",
                        EnglishName = "Bonfire Burning Speed Coefficient",
                        GameKey = nameof(BuildingSettings.YingHuoRanShaoSuDuRatio),
                        Description = "Multiplies the speed at which bonfires will burn fuel. Higher values = shorter burn time",
                        Tooltip = "The greater the value, the more fuel is consumed for Bonfires.",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "个人最大可建造营火数量",
                        EnglishName = "Limit of Personal Bonfires",
                        GameKey = nameof(BuildingSettings.MaxGenRenYingHuoNumber),
                        Description = "Sets the number of bonfires that a single player can have constructed at once.",
                        Min = 1.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "部落最大可建造营火数量",
                        EnglishName = "Limit of Tribal Bonfires",
                        GameKey = nameof(BuildingSettings.MaxGongHuiYingHuoNumber),
                        Description = "Sets the number of bonfires that a tribe can have constructed at once.",
                        Min = 1.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "最大可建造传送门数量",
                        EnglishName = "Limit of Portals",
                        GameKey = nameof(BuildingSettings.MaxChuanSongMenNumber),
                        Min = 10.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "最大休眠舱数量",
                        EnglishName = "Max Hibernation Pods",
                        GameKey = nameof(BuildingSettings.MaxXiuMianCangCount),
                        Min = 1.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "巨象与船只上最大建筑建造数量倍率",
                        EnglishName = "Giant Elephant and Ship Max Construction Quantity Multiplier",
                        GameKey = nameof(BuildingSettings.MaxPingTaiJianZhuNumMul),
                        Description = "Multiplies the maximum number of building parts that can be attached to ships and elephants. The values need more testing, but based on a statement from the developers, these should be the values when using the default 0.1 multiplier: Thatch Boat: 4 buildable parts Small Wooden Boat: 30 buildable parts Falcon-class Airship: 60 buildable parts Shark-class Flying Ship: 100 buildable parts",
                        Tooltip = "The higher the value, the higher the construction limit on Giant Elephants and Ships.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "新营火认定期时长系数",
                        EnglishName = "New Bonfire Recognition Period Duration Coefficient",
                        GameKey = nameof(BuildingSettings.NewYingHuoTimeLenMul),
                        Tooltip = "Newly built bonfires have a 30-minute recognition period. During this time in PVP Mode, the function to ignore other players' Protective Field within your bonfire range is disabled. Set this coefficient to 0 to remove this period.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                }
            },
            new ParameterCategory
            {
                ChineseName = "刷新",
                EnglishName = "Refresh",
                Params = new List<ParameterDef>
                {
                    new ParameterDef
                    {
                        ChineseName = "植被重生速度",
                        EnglishName = "Vegetation Respawn Speed",
                        GameKey = nameof(RefreshSettings.ZhiBeiChongShengRatio),
                        Description = "Multiplies the speed at which collectible vegetation will respawn. Higher values = shorter respawn time",
                        Tooltip = "The greater the value, the faster it is for vegetation to reload.",
                        Min = 0.1,
                        Max = 10.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "禁止资源刷新半径-玩家",
                        EnglishName = "Banned resource refresh radius - Players",
                        GameKey = nameof(RefreshSettings.WanJiaZiYuanJinShuaBanJing),
                        Description = "Multiplies the radius around players where resource objects will not respawn.",
                        Tooltip = "Banned resource refresh radius - Players",
                        Min = 0.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "禁止资源刷新半径-建筑物",
                        EnglishName = "Banned resource refresh radius - Buildings",
                        GameKey = nameof(RefreshSettings.JianZhuZiYuanJinShuaBanJing),
                        Description = "Multiplies the radius around player owned buildings where resource objects will not respawn.",
                        Tooltip = "Banned resource refresh radius - Buildings",
                        Min = 0.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                }
            },
            new ParameterCategory
            {
                ChineseName = "战斗",
                EnglishName = "Combat",
                Params = new List<ParameterDef>
                {
                    new ParameterDef
                    {
                        ChineseName = "对野生怪物伤害倍率",
                        EnglishName = "DMG Multiplier against wild monsters",
                        GameKey = nameof(CombatSettings.DamageYeShengRatio),
                        Description = "Multiplies damage dealt to all types of wild NPCs.",
                        Tooltip = "The greater the value, the greater the damage the player deals to wild animals or machines.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "被野生怪物攻击受伤害倍率",
                        EnglishName = "DMG Multiplier when attacked by wild monsters",
                        GameKey = nameof(CombatSettings.BeDamageByYeShengRatio),
                        Description = "Multiplies damage dealth by all types of wild NPCs.",
                        Tooltip = "The greater the value, the greater the damage the player takes from wild animals or machines.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "生命恢复速度倍率",
                        EnglishName = "HP Recovery Speed Multiplier",
                        GameKey = nameof(CombatSettings.ShengMingHuiFuRatio),
                        Description = "Multiplies the rate at which HP passively recovers. Higher value = faster recovery. Seems to affect anything that passively recovers HP.",
                        Tooltip = "The greater the value, the higher the default HP Recovery.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "体力恢复速度倍率",
                        EnglishName = "Stamina Recovery Speed Multiplier",
                        GameKey = nameof(CombatSettings.TiLiHuiFuRatio),
                        Description = "Multiplies the rate at which stamina passively recovers. Higher value = faster recovery. Seems to affect anything that passively recovers stamina.",
                        Tooltip = "The greater the value, the higher the default Stamina Recovery.",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "气息恢复速度倍率",
                        EnglishName = "Breath Recovery Speed Multiplier",
                        GameKey = nameof(CombatSettings.QiXiHuiFuRatio),
                        Description = "The speed at which the breath meter refills when not underwater. Higher values = shorter time to refill",
                        Tooltip = "The greater the value, the higher the default oxygen recovery rate.",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "野生动物攻击伤害倍率",
                        EnglishName = "DMG Multiplier of wild animal attacks",
                        GameKey = nameof(CombatSettings.DongWuDamageRatio),
                        Description = "Multiplier of damage dealt by wild animals and mechanical NPCs. Stacks with “DMG Multiplier when attacked by wild monsters” setting.",
                        Tooltip = "The greater the value, the greater the damage dealt to players by wild animals and machines.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "野生动物受到伤害倍率",
                        EnglishName = "DMG Multiplier against wild animals",
                        GameKey = nameof(CombatSettings.DongWuJianShangRatio),
                        Description = "Multiplier of damage dealt to wild animals and mechanical NPCs. Stacks with “DMG Multiplier against wild monsters” setting.",
                        Tooltip = "The greater the value, the greater the damage the wild animals or machines take.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "野生蛮人攻击伤害倍率",
                        EnglishName = "DMG Multiplier of wild barbarian attacks",
                        GameKey = nameof(CombatSettings.MaRenDamageRatio),
                        Description = "Multiplier of damage dealt by wild humans. Stacks with “DMG Multiplier when attacked by wild monsters” setting.",
                        Tooltip = "The greater the value, the greater the damage dealt to players by wild barbarians.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "野生蛮人受到伤害倍率",
                        EnglishName = "DMG Multiplier against wild barbarians",
                        GameKey = nameof(CombatSettings.ManRenJianShangRatio),
                        Description = "Multiplier of damage dealt to wild humans. Stacks with “DMG Multiplier against wild monsters” setting.",
                        Tooltip = "The greater the value, the greater the damage dealt to wild barbarians.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "假死状态生命恢复速度",
                        EnglishName = "Feign Death HP Recovery Speed",
                        GameKey = nameof(CombatSettings.JiaSiHuiFuRatio),
                        Description = "Multiplier for HP recovery speed while in feign death state.",
                        Tooltip = "The greater the value, the faster the HP recovery while in feign death state.",
                        Min = 0,
                        Max = 10,
                        Step = 0.001F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "攻击建筑的伤害倍率",
                        EnglishName = "Damage Multiplier when attacking buildings.",
                        GameKey = nameof(CombatSettings.GongJiJianZhuDamageRatio),
                        Description = "Multiplies damage dealt by players and player owned entities against player owned buildings.",
                        Tooltip = "The higher the value, the more the damage dealt to buildings.",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "野生动物品质系数",
                        EnglishName = "Wild Animal Quality Coefficient",
                        GameKey = nameof(CombatSettings.DongWuPinZhiRatio),
                        Tooltip = "Common Barbarian gear initial durability ratio multiplier (Max 100% durability; In PVP mode, only affects advanced weapons/armor exceeding the current server level limit)",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "野生蛮人品质系数",
                        EnglishName = "Wild Barbarian Quality Coefficient",
                        GameKey = nameof(CombatSettings.ManRenPinZhiRatio),
                        Description = "Affects the chance of wild humans spawning at a higher quality level. Higher values = increased chance to be higher quality",
                        Tooltip = "The greater the value, the more chances of getting quality barbarians.",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "PVP非玩家操控的友方对象之间的伤害倍率",
                        EnglishName = "DMG Multiplier among friendly units operated by non-players in the PvP mode",
                        GameKey = nameof(CombatSettings.PVP_ShangHaiRatio_WithoutP2P_YouFang),
                        Description = "Multiplier on damage dealt by AI controlled units to other units in their tribe. Disabled if PVP is disabled.",
                        Tooltip = "The higher the value, the greater the damage between non-protagonists in PVP.",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 0.001,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "PVP近战伤害系数",
                        EnglishName = "PVP melee damage coefficient",
                        GameKey = nameof(CombatSettings.PVP_ShangHaiRatio_JinZhan),
                        Description = "Multiplier on melee damage dealt to enemy units in PVP.",
                        Tooltip = "The higher the value, the greater the PVP melee damage.",
                        Min = 0.0,
                        Max = 1.0,
                        Step = 0.001F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "PVP远程伤害系数",
                        EnglishName = "PVP ranged damage coefficient",
                        GameKey = nameof(CombatSettings.PVP_ShangHaiRatio_YuanCheng),
                        Description = "Multiplier on ranged damage dealt to enemy units in PVP.",
                        Tooltip = "The higher the value, the greater the ranged damage in PVP.",
                        Min = 0.0,
                        Max = 1.0,
                        Step = 0.001F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "PVP玩家被削韧系数",
                        EnglishName = "PVP RESIL-Down Coefficient",
                        GameKey = nameof(CombatSettings.WanJiaBeiXiaoRenRatio),
                        Description = "Multiplier on resilience reduction amount against enemy units in PVP.",
                        Tooltip = "The higher the value, the more the Resilience deducted when a PVP player is attacked.",
                        Min = 0.0,
                        Max = 1.0,
                        Step = 0.001F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "PVP玩家被削体系数",
                        EnglishName = "PVP STA-Down Coefficient",
                        GameKey = nameof(CombatSettings.WanJiaBeiXiaoTiRatio),
                        Description = "Multiplier on stamina reduction amount against enemy units in PVP.",
                        Tooltip = "The higher the value, the more the Stamina deducted when a blocking PVP player is attacked.",
                        Min = 0.0,
                        Max = 1.0,
                        Step = 0.001F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "PVP敌对玩家之间的伤害系数",
                        EnglishName = "Damage coefficient between enemies in PVP",
                        GameKey = nameof(CombatSettings.PVP_ShangHaiRatio_PlayerToPlayer_DiFang),
                        Description = "Additional PVP damage multiplier applied when the source and target are both player controlled characters.",
                        Tooltip = "The higher the value, the greater the damage between enemies in PVP.",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "PVP技能百分比真实伤害倍率",
                        EnglishName = "PVP True Damage Adjustment Coefficient",
                        GameKey = nameof(CombatSettings.PVP_GAPVPDamageRatio),
                        Tooltip = "The higher the value, the greater the PVP skill percentage True Damage.",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "PVP友方玩家之间的伤害系数",
                        EnglishName = "Damage coefficient between allies in PVP",
                        GameKey = nameof(CombatSettings.PVP_ShangHaiRatio_PlayerToPlayer_YouFang),
                        Description = "Multiplier applied to friendly fire damage when the source and target are both players. Disabled if PVP is disabled.",
                        Tooltip = "The higher the value, the greater the damage between allies in PVP.",
                        Min = 0.0,
                        Max = 1.0,
                        Step = 0.001F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "翻滚无敌时间倍率",
                        EnglishName = "Roll Invulnerability Time Multiplier",
                        GameKey = nameof(CombatSettings.RollingInvincibleTimeRatio),
                        Description = "Multiplier for invulnerability duration when rolling.",
                        Tooltip = "The greater the value, the longer the invulnerability duration for the roll.",
                        Min = 0.5,
                        Max = 1.0,
                        Step = 0.001F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "体力恢复间隔时间系数",
                        EnglishName = "Stamina Recovery Delay Coefficient",
                        GameKey = nameof(CombatSettings.PhysicalRecoveryIntervalRate),
                        Tooltip = "Stamina Recovery Delay Multiplier. The higher the value, the longer the time required before stamina recovery starts after using stamina.",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "蛮人削体系数",
                        EnglishName = "Barbarian STA-Down Multiplier",
                        GameKey = nameof(CombatSettings.ManRenTiLiDamageRatio),
                        Tooltip = "The higher the value, the more the Stamina deducted when blocking attacks from Wild Barbarians.",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "蛮人削韧系数",
                        EnglishName = "Barbarian RESIL-Down Multiplier",
                        GameKey = nameof(CombatSettings.ManRenTenacityDamageRatio),
                        Tooltip = "The higher the value, the more the Resilience deducted when attacked by Wild Barbarians.",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "蛮人Boss削体系数",
                        EnglishName = "Barbarian Boss STA-Down Multiplier",
                        GameKey = nameof(CombatSettings.ManRenBossTiLiDamageRatio),
                        Tooltip = "The higher the value, the more the Stamina deducted when blocking attacks from Wild Barbarian Bosses.",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "蛮人Boss削韧系数",
                        EnglishName = "Barbarian Boss RESIL-Down Multiplier",
                        GameKey = nameof(CombatSettings.ManRenBossTenacityDamageRatio),
                        Tooltip = "The higher the value, the more the Resilience deducted when attacked by Wild Barbarian Bosses.",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "野生动物（机械体）削体系数",
                        EnglishName = "Wild Animal (mechanical bodies) STA-Down Multiplier",
                        GameKey = nameof(CombatSettings.DongWuTiLiDamageRatio),
                        Tooltip = "The higher the value, the more the Stamina deducted when blocking attacks from Wild Animals (mechanical bodies).",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "野生动物（机械体）削韧系数",
                        EnglishName = "Wild Animal (mechanical bodies) RESIL-Down Multiplier",
                        GameKey = nameof(CombatSettings.DongWuTenacityDamageRatio),
                        Tooltip = "The higher the value, the more the Resilience deducted when attacked by Wild Animals (mechanical bodies).",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "野生动物（机械体）Boss削体系数",
                        EnglishName = "Wild Animal (mechanical bodies) Boss STA-Down Multiplier",
                        GameKey = nameof(CombatSettings.DongWuBossTiLiDamageRatio),
                        Tooltip = "The higher the value, the more the Stamina deducted when blocking attacks from Wild Animal (mechanical bodies) Bosses.",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "野生动物（机械体）Boss削韧系数",
                        EnglishName = "Wild Animal (mechanical bodies) Boss RESIL-Down Multiplier",
                        GameKey = nameof(CombatSettings.DongWuBossTenacityDamageRatio),
                        Tooltip = "The higher the value, the more the Resilience deducted when attacked by Wild Animal (mechanical bodies) Bosses.",
                        Min = 0.0,
                        Max = 5.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "PVE玩家伤害检查范围缩放倍率",
                        EnglishName = "PVE Player Damage Check Range Scaling Multiplier",
                        GameKey = nameof(CombatSettings.PlayerSweepRangeScale),
                        Tooltip = "PVE Player Damage Check Range Scaling Multiplier",
                        Min = 0.1,
                        Max = 10.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "弹反难度",
                        EnglishName = "Reflect Difficulty",
                        GameKey = nameof(CombatSettings.ReboundDifficulty),
                        Tooltip = "Effective only in PVE environments",
                        Min = 0.0,
                        Max = 2.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "玩家角色解除控制技能\"意识苏醒\"冷却系数",
                        EnglishName = "\"Recover Sense\" CC Break Cooldown Modifier",
                        GameKey = nameof(CombatSettings.ReleaseControlStatusCDRatio),
                        Tooltip = "Used to adjust the CD of the player character's control removal skill \"Recover Sense\"",
                        Min = 0,
                        Max = 1,
                        Step = 0.001F,
                        Type = ParameterType.Float,
                    },
                }
            },
            new ParameterCategory
            {
                ChineseName = "消耗",
                EnglishName = "Consumption",
                Params = new List<ParameterDef>
                {
                    new ParameterDef
                    {
                        ChineseName = "耐久消耗倍率",
                        EnglishName = "Durability Consumption Multiplier",
                        GameKey = nameof(ConsumptionSettings.NaiJiuXiShu),
                        Tooltip = "Barbarian Boss gear initial durability ratio multiplier (Max 100% durability; In PVP mode, only affects advanced weapons/armor exceeding the current server level limit)",
                        Min = 0.0,
                        Max = 2.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "食物消耗速度倍率",
                        EnglishName = "Satiety Consumption Speed Multiplier",
                        GameKey = nameof(ConsumptionSettings.ShiWuXiaoHaoRatio),
                        Description = "Multiplies the rate at which satiety is reduced over time. Higher values = faster reduction",
                        Tooltip = "The greater the value, the greater the food consumption.",
                        Min = 0.0,
                        Max = 2.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "水分消耗速度倍率",
                        EnglishName = "Hydration Consumption Speed Multiplier",
                        GameKey = nameof(ConsumptionSettings.ShuiXiaoHaoRatio),
                        Description = "Multiplies the rate at which hydration is reduced over time. Higher values = faster reduction",
                        Tooltip = "The greater the value, the faster the dehydration.",
                        Min = 0.0,
                        Max = 2.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "气息消耗速度倍率",
                        EnglishName = "Breath Cost Speed Multiplier",
                        GameKey = nameof(ConsumptionSettings.QiXiXiaoHaoRatio),
                        Description = "Multiplies the rate at which the breath meter decreases while underwater. Higher = faster reduction",
                        Tooltip = "The greater the value, the more oxygen is consumed when underwater",
                        Min = 0.0,
                        Max = 2.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "燃料消耗速度",
                        EnglishName = "Fuel consumption rate",
                        GameKey = nameof(ConsumptionSettings.RanLiaoXiaoHaoRatio),
                        Description = "Multiplies the rate at which fuel is burned by anything that burns fuel. Higher values = shorter burn time",
                        Tooltip = "The higher the value, the higher the fuel consumption rate.",
                        Min = 0.0,
                        Max = 2.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "动物食物消耗速度",
                        EnglishName = "Animal Food Consumption Speed",
                        GameKey = nameof(ConsumptionSettings.DongWuXiaoHaoShiWuRatio),
                        Description = "Maximum number of animals that can follow a player at once.",
                        Tooltip = "Max followable animals",
                        Min = 0.0,
                        Max = 2.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "动物水分消耗速度",
                        EnglishName = "Animal Water Consumption Speed",
                        GameKey = nameof(ConsumptionSettings.DongWuXiaoHaoShuiRatio),
                        Tooltip = "The higher the value, the faster the water consumption speed of bred animals.",
                        Min = 0.0,
                        Max = 2.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "作物肥料消耗速度",
                        EnglishName = "Crop Fertilizer Consumption Speed",
                        GameKey = nameof(ConsumptionSettings.ZuoWuFeiLiaoXiaoHaoRatio),
                        Description = "Multiplies the amount of fertilizer consumed by crops as they grow. Higher values = more fertilizer consumed",
                        Tooltip = "The higher the value, the higher the crop fertilizer consumption speed.",
                        Min = 0.0,
                        Max = 2.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "作物水分消耗速度",
                        EnglishName = "Crop Water Consumption Speed",
                        GameKey = nameof(ConsumptionSettings.ZuoWuShuiXiaoHaoRatio),
                        Description = "Multiplies the amount of water consumed by crops as they grow. Higher values = more water consumed",
                        Tooltip = "The higher the value, the higher the crop water consumption speed.",
                        Min = 0.0,
                        Max = 2.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "物品腐坏所需时间倍率",
                        EnglishName = "Time Multiplier required for items to decay",
                        GameKey = nameof(ConsumptionSettings.WuPinFuHuaiRatio),
                        Description = "Multiplies the time it takes for dropped items to be destroyed. Higher values = longer time",
                        Tooltip = "The greater the value, the longer it takes to decay.",
                        Min = 0.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "死亡包裹自动销毁所需时间倍率",
                        EnglishName = "Time Multiplier for auto-destruction of Death Packages",
                        GameKey = nameof(ConsumptionSettings.WuPinXiaoHuiTime),
                        Description = "Multiplies the time it takes for items dropped on death to be destroyed. Higher values = longer time",
                        Tooltip = "The greater the value, the longer it takes to destroy a Death Pack.",
                        Min = 0.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "修理所需材料倍率",
                        EnglishName = "Coefficient of materials needed for the repair",
                        GameKey = nameof(ConsumptionSettings.XiuLiXuYaoCaiLiaoRatio),
                        Description = "Multiplies the amount of materials needed to repair items.",
                        Tooltip = "The smaller the value, the fewer materials required for repairs, based on the initial repair cost.",
                        Min = 0.0,
                        Max = 1.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "修理降低耐久上限系数",
                        EnglishName = "Repair reduces the durability limit coefficient",
                        GameKey = nameof(ConsumptionSettings.XiuLiJiangNaiJiuShangXianRatio),
                        Description = "Multiplies the amount of maximum durability lost when repairing an item",
                        Tooltip = "The lesser the value, the lower the Max Durability.",
                        Min = 0.0,
                        Max = 1.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                }
            },
            new ParameterCategory
            {
                ChineseName = "入侵",
                EnglishName = "Invasion",
                Params = new List<ParameterDef>
                {
                    new ParameterDef
                    {
                        ChineseName = "入侵热度积累速率",
                        EnglishName = "Invasion Fever Accumulation Rate",
                        GameKey = nameof(InvasionSettings.ReDuXiShu),
                        Tooltip = "The higher the value, the larger the scale of the invading monsters.",
                        Min = 0.1,
                        Max = 100.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "入侵怪物规模系数",
                        EnglishName = "Invasion Monster Scale Coefficient",
                        GameKey = nameof(InvasionSettings.RuQinGuiMoXiShu),
                        Tooltip = "The higher the value, the stronger the Strength of invading monsters.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "入侵怪物强度系数",
                        EnglishName = "Invasion Monster Strength Coefficient",
                        GameKey = nameof(InvasionSettings.RuQinQiangDuXiShu),
                        Description = "Multiplies the level of all units that are part of an invasion force.",
                        Tooltip = "The higher the value, the higher the level of invading monsters.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "入侵怪物总量下限",
                        EnglishName = "Invasion Monster Total Qty Lower Limit",
                        GameKey = nameof(InvasionSettings.RuQinGuaiCountMin),
                        Description = "Sets the lowest number of enemies that can spawn in an invasion.",
                        Tooltip = "The smaller the value, the lower the Invasion Monster Total Qty Lower Limit.",
                        Min = 1.0,
                        Max = 50.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "入侵怪物总量上限",
                        EnglishName = "Invasion Monster Total Qty Upper Limit",
                        GameKey = nameof(InvasionSettings.RuQinGuaiCountMax),
                        Description = "Sets the highest number of enemies that can spawn in an invasion.",
                        Tooltip = "The higher the value, the higher the Invasion Monster Total Qty Upper Limit.",
                        Min = 2.0,
                        Max = 256.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "入侵每波数量下限",
                        EnglishName = "Invasion Min Wave Qty",
                        GameKey = nameof(InvasionSettings.RuQinPerBoGuaiMin),
                        Description = "Sets the lowest number of enemies that can spawn in a single wave of an invasion.",
                        Tooltip = "The smaller the value, the smaller the Invasion Min Wave Qty.",
                        Min = 1.0,
                        Max = 50.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "入侵每波数量上限",
                        EnglishName = "Invasion Max Wave Qty",
                        GameKey = nameof(InvasionSettings.RuQinPerBoGuaiMax),
                        Description = "Sets the highest number of enemies that can spawn in a single wave of an invasion.",
                        Tooltip = "The higher the value, the higher the Invasion Max Wave Qty.",
                        Min = 1.0,
                        Max = 256.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "入侵怪物等级系数",
                        EnglishName = "Invasion Monster Level Coefficient",
                        GameKey = nameof(InvasionSettings.RuQinGuaiLevelXiShu),
                        Description = "Multiplies the level of all units that are part of an invasion force.",
                        Tooltip = "The higher the value, the higher the level of invading monsters.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "入侵探查分钟数",
                        EnglishName = "Invasion Scouting Minutes",
                        GameKey = nameof(InvasionSettings.TanChaMinuteLimit),
                        Description = "Sets the lowest number of enemies that can spawn in an invasion.",
                        Tooltip = "The smaller the value, the lower the Invasion Monster Total Qty Lower Limit.",
                        Min = 1.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "入侵进攻分钟数",
                        EnglishName = "Invasion Attack Minutes",
                        GameKey = nameof(InvasionSettings.JinGongMinuteLimit),
                        Description = "The maximum duration of an invasion.",
                        Tooltip = "Invasion Attack Minutes",
                        Min = 30.0,
                        Max = 120.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "入侵冷却分钟数",
                        EnglishName = "Invasion Cooldown Minutes",
                        GameKey = nameof(InvasionSettings.LengQueMinuteLimit),
                        Description = "The minimum amount of time that must pass between invasions which target the same tribe.",
                        Tooltip = "Invasion Cooldown Minutes",
                        Min = 1.0,
                        Max = 14400.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "入侵同时进行场次限制",
                        EnglishName = "Invasion Simultaneous Sessions Limit",
                        GameKey = nameof(InvasionSettings.RuQinMaxChangCiCount),
                        Tooltip = "The number of invasions a tribe can face at the same time.",
                        Min = 1.0,
                        Max = 5.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "入侵开始时间",
                        EnglishName = "Invasion Start Time",
                        GameKey = nameof(InvasionSettings.RuQinBeginHour),
                        Description = "These settings allow restricting PVP activities to certain times of day. If PVP is disabled, these settings will have no effect. If PVP is enabled, then PVP will be allowed only between the defined start and end times. The times use UTC as the time zone regardless of region. “Working Day” seems to refer to Monday through Friday and “Non-working Day” seems to be Saturday and Sunday. Testing so far indicates that private servers seem to always use the PVP time settings from the Asia region, but you may want to configure all regions just to be sure.",
                        Tooltip = "Duration for Attacking Other Player's Building",
                        Min = 0.0,
                        Max = 23.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "入侵结束时间",
                        EnglishName = "Invasion End Time",
                        GameKey = nameof(InvasionSettings.RuQinEndHour),
                        Tooltip = "AS Server Event End Time (UTC)",
                        Min = 1.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "入侵烧城系数",
                        EnglishName = "Invasion Siege Coefficient",
                        GameKey = nameof(InvasionSettings.RuQinShaoChengXiShu),
                        Tooltip = "The higher the value, the more invading monsters incline toward destroying buildings.",
                        Min = 0.0,
                        Max = 1.0,
                        Step = 0.001F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "入侵屠杀系数",
                        EnglishName = "Invasion Slaughter Coefficient",
                        GameKey = nameof(InvasionSettings.RuQinTuShaXiShu),
                        Tooltip = "The higher the value, the more invading monsters incline toward killing tribesmen.",
                        Min = 0.0,
                        Max = 1.0,
                        Step = 0.001F,
                        Type = ParameterType.Float,
                    },
                    new ParameterDef
                    {
                        ChineseName = "连续成功抵御入侵发放奖励间隔次数",
                        EnglishName = "Invasion Defense Success Reward Interval",
                        GameKey = nameof(InvasionSettings.RuQinSucceedPrizeTimes),
                        Tooltip = "Invasion Defense Success Reward Interval",
                        Min = 1.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "经营模式入侵冷却时间倍率",
                        EnglishName = "Invasion Countdown Multiplier in Tribe Mode",
                        GameKey = nameof(InvasionSettings.ManageModeRuQinCountDownTimeRatio),
                        Tooltip = "The higher the value, the longer the invasion countdown in Tribe Mode.",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 0.01F,
                        Type = ParameterType.Float,
                        IsEnabled = false,
                    },
                }
            },
            new ParameterCategory
            {
                ChineseName = "PVP设置",
                EnglishName = "PVP Settings",
                Params = new List<ParameterDef>
                {
                    new ParameterDef
                    {
                        ChineseName = "亚服工作日PVP开始时间（UTC时间）",
                        EnglishName = "Asian Server Working Day PVP Start Time (UTC)",
                        GameKey = nameof(PVPSettingSettings.PVPTimeAsiaWorkStartTime),
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "亚服工作日PVP结束时间（UTC时间）",
                        EnglishName = "Asian Server Working Day PVP End Time (UTC)",
                        GameKey = nameof(PVPSettingSettings.PVPTimeAsiaWorkEndTime),
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "亚服非工作日PVP开始时间（UTC时间）",
                        EnglishName = "Asian Server Non-working Day PVP Start Time (UTC)",
                        GameKey = nameof(PVPSettingSettings.PVPTimeAsiaNoWorkStartTime),
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "亚服非工作日PVP结束时间（UTC时间）",
                        EnglishName = "Asian Server Non-working Day PVP End Time (UTC)",
                        GameKey = nameof(PVPSettingSettings.PVPTimeAsiaNoWorkEndTime),
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "美服工作日PVP开始时间（UTC时间）",
                        EnglishName = "US Server Working Day PVP Start Time (UTC)",
                        GameKey = nameof(PVPSettingSettings.PVPTimeAmericaWorkStartTime),
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "美服工作日PVP结束时间（UTC时间）",
                        EnglishName = "US Server Working Day PVP End Time (UTC)",
                        GameKey = nameof(PVPSettingSettings.PVPTimeAmericaWorkEndTime),
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "美服非工作日PVP开始时间（UTC时间）",
                        EnglishName = "US Server Non-working Day PVP Start Time (UTC)",
                        GameKey = nameof(PVPSettingSettings.PVPTimeAmericaNoWorkStartTime),
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "美服非工作日PVP结束时间（UTC时间）",
                        EnglishName = "US Server Non-working Day PVP End Time (UTC)",
                        GameKey = nameof(PVPSettingSettings.PVPTimeAmericaNoWorkEndTime),
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "欧服工作日PVP开始时间（UTC时间）",
                        EnglishName = "European Server Working Day PVP Start Time (UTC)",
                        GameKey = nameof(PVPSettingSettings.PVPTimeEuropeWorkStartTime),
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "欧服工作日PVP结束时间（UTC时间）",
                        EnglishName = "European Server Working Day PVP End Time (UTC)",
                        GameKey = nameof(PVPSettingSettings.PVPTimeEuropeWorkEndTime),
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "欧服非工作日PVP开始时间（UTC时间）",
                        EnglishName = "European Server Non-working Day PVP Start Time (UTC)",
                        GameKey = nameof(PVPSettingSettings.PVPTimeEuropeNoWorkStartTime),
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "欧服非工作日PVP结束时间（UTC时间）",
                        EnglishName = "European Server Non-working Day PVP End Time (UTC)",
                        GameKey = nameof(PVPSettingSettings.PVPTimeEuropeNoWorkEndTime),
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "创建角色时默认意识等级",
                        EnglishName = "Default Awareness Level on Character Creation",
                        GameKey = nameof(PVPSettingSettings.InitialDefaultAwarenessLevel),
                        Tooltip = "Default Awareness Level on Character Creation",
                        Min = 1.0,
                        Max = 60.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "开服第一天意识等级上限",
                        EnglishName = "Server Day 1 Awareness Level Cap",
                        GameKey = nameof(PVPSettingSettings.FirstDayMaxAwarenessLevel),
                        Tooltip = "Awareness Level Cap on Server Day 1 after Launch",
                        Min = 1.0,
                        Max = 60.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "开服第二天意识等级上限",
                        EnglishName = "Server Day 2 Awareness Level Cap",
                        GameKey = nameof(PVPSettingSettings.SecondDayMaxAwarenessLevel),
                        Tooltip = "Awareness Level Cap on Server Day 2 after Launch",
                        Min = 1.0,
                        Max = 60.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "开服第三天意识等级上限",
                        EnglishName = "Server Day 3 Awareness Level Cap",
                        GameKey = nameof(PVPSettingSettings.ThirdDayMaxAwarenessLevel),
                        Tooltip = "Awareness Level Cap on Server Day 3 after Launch",
                        Min = 1.0,
                        Max = 60.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "开服第四天意识等级上限",
                        EnglishName = "Server Day 4 Awareness Level Cap",
                        GameKey = nameof(PVPSettingSettings.FourthDayMaxAwarenessLevel),
                        Tooltip = "Awareness Level Cap on Server Day 4 after Launch",
                        Min = 1.0,
                        Max = 60.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "开服第五天意识等级上限",
                        EnglishName = "Server Day 5 Awareness Level Cap",
                        GameKey = nameof(PVPSettingSettings.FifthDayMaxAwarenessLevel),
                        Tooltip = "Awareness Level Cap on Server Day 5 after Launch",
                        Min = 1.0,
                        Max = 60.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "开服第六天意识等级上限",
                        EnglishName = "Server Day 6 Awareness Level Cap",
                        GameKey = nameof(PVPSettingSettings.SixthDayMaxAwarenessLevel),
                        Tooltip = "Awareness Level Cap on Server Day 6 after Launch",
                        Min = 1.0,
                        Max = 60.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "开服第七天意识等级上限",
                        EnglishName = "Server Day 7 Awareness Level Cap",
                        GameKey = nameof(PVPSettingSettings.SeventhDayMaxAwarenessLevel),
                        Tooltip = "Awareness Level Cap on Server Day 7 after Launch",
                        Min = 1.0,
                        Max = 60.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "开服第八天意识等级上限",
                        EnglishName = "Server Day 8 Awareness Level Cap",
                        GameKey = nameof(PVPSettingSettings.EighthDayMaxAwarenessLevel),
                        Tooltip = "Awareness Level Cap on Server Day 8 after Launch",
                        Min = 1.0,
                        Max = 60.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "开服第九天意识等级上限",
                        EnglishName = "Server Day 9 Awareness Level Cap",
                        GameKey = nameof(PVPSettingSettings.NinthDayMaxAwarenessLevel),
                        Tooltip = "Awareness Level Cap on Server Day 9 after Launch",
                        Min = 1.0,
                        Max = 60.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "开服第十天意识等级上限",
                        EnglishName = "Server Day 10 Awareness Level Cap",
                        GameKey = nameof(PVPSettingSettings.TenthDayMaxAwarenessLevel),
                        Tooltip = "Awareness Level Cap on Server Day 10 after Launch",
                        Min = 1.0,
                        Max = 60.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                }
            },
            new ParameterCategory
            {
                ChineseName = "AI相关",
                EnglishName = "AI Settings",
                Params = new List<ParameterDef>
                {
                    new ParameterDef
                    {
                        ChineseName = "野生敌人的AI级别",
                        EnglishName = "Wild Enemy AI Level",
                        GameKey = nameof(AISettings.AIDengJi),
                        Description = "Controls the difficulty level of wild enemy AI. Higher values make enemies more aggressive and smarter.",
                        Tooltip = "Wild Enemy AI Level (1-3)",
                        Min = 1.0,
                        Max = 3.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "族人出战数量",
                        EnglishName = "Deployed Tribesmen",
                        GameKey = nameof(AISettings.ManRenChuZhanCount),
                        Description = "The number of tribesmen that can be deployed by a player at one time.",
                        Min = 1.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "可出战的动物的最大数量",
                        EnglishName = "Animals Deployed",
                        GameKey = nameof(AISettings.DongWuChuZhanCount),
                        Tooltip = "",
                        Min = 1.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                        IsEnabled = false,
                    },
                }
            },
            new ParameterCategory
            {
                ChineseName = "战场时间",
                EnglishName = "Battle Time",
                Params = new List<ParameterDef>
                {
                    new ParameterDef
                    {
                        ChineseName = "亚服战场起始时间",
                        EnglishName = "Asia Server Battle Start Time",
                        GameKey = nameof(BattleTimeSettings.AsiaWarTimeStart),
                        Tooltip = "EU Server Event Start Time (UTC, applies to all events)",
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "亚服战场结束时间",
                        EnglishName = "Asia Server Battle End Time",
                        GameKey = nameof(BattleTimeSettings.AsiaWarTimeEnd),
                        Tooltip = "EU Server Event End Time (UTC)",
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "欧服战场起始时间",
                        EnglishName = "EU Server Battle Start Time",
                        GameKey = nameof(BattleTimeSettings.EuropeWarTimeStart),
                        Tooltip = "AS Server Event Start Time (UTC, applies to all events)",
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "欧服战场结束时间",
                        EnglishName = "EU Server Battle End Time",
                        GameKey = nameof(BattleTimeSettings.EuropeWarTimeEnd),
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "美服战场起始时间",
                        EnglishName = "NA/SA Server Battle Start Time",
                        GameKey = nameof(BattleTimeSettings.AmericaWarTimeStart),
                        Tooltip = "EU Server Event Start Time (UTC, applies to all events)",
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "美服战场结束时间",
                        EnglishName = "NA/SA Server Battle End Time",
                        GameKey = nameof(BattleTimeSettings.AmericaWarTimeEnd),
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                }
            },
            new ParameterCategory
            {
                ChineseName = "全服事件",
                EnglishName = "Server Event",
                Params = new List<ParameterDef>
                {
                    new ParameterDef
                    {
                        ChineseName = "全服事件大区设置",
                        EnglishName = "Server Event Region Settings",
                        GameKey = nameof(ServerEventSettings.SpecialEventGameDist),
                        Min = 0.0,
                        Max = 2.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "亚服全服事件开始时间",
                        EnglishName = "AS Server Event Start Time",
                        GameKey = nameof(ServerEventSettings.SpecialEventAsiaStartTime),
                        Tooltip = "AS Server Event Start Time (UTC, applies to all events)",
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "亚服全服事件结束时间",
                        EnglishName = "AS Server Event End Time",
                        GameKey = nameof(ServerEventSettings.SpecialEventAsiaEndTime),
                        Tooltip = "AS Server Event End Time (UTC)",
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "欧服全服事件开始时间",
                        EnglishName = "EU Server Event Start Time",
                        GameKey = nameof(ServerEventSettings.SpecialEventEuropeStartTime),
                        Tooltip = "EU Server Event Start Time (UTC, applies to all events)",
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "欧服全服事件结束时间",
                        EnglishName = "EU Server Event End Time",
                        GameKey = nameof(ServerEventSettings.SpecialEventEuropeEndTime),
                        Tooltip = "EU Server Event End Time (UTC)",
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "美服全服事件开始时间",
                        EnglishName = "NA Server Event Start Time",
                        GameKey = nameof(ServerEventSettings.SpecialEventAmericaStartTime),
                        Tooltip = "NA Server Event Start Time (UTC, applies to all events)",
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "美服全服事件结束时间",
                        EnglishName = "NA Server Event End Time",
                        GameKey = nameof(ServerEventSettings.SpecialEventAmericaEndTime),
                        Tooltip = "NA Server Event End Time (UTC)",
                        Min = 0.0,
                        Max = 24.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "全服事件触发间隔",
                        EnglishName = "Server Event Trigger Interval",
                        GameKey = nameof(ServerEventSettings.SpecialEventTriggerInterval),
                        Tooltip = "Server event trigger interval, in seconds (this interval applies to all server events.)",
                        Min = 1.0,
                        Max = 86399.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "全服事件触发概率",
                        EnglishName = "Server Event Trigger Chance",
                        GameKey = nameof(ServerEventSettings.SpecialEventTriggerPercent),
                        Tooltip = "Server event trigger chance (100 is 100%, this chance applies to all server events.)",
                        Min = 0.0,
                        Max = 100.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "全服事件触发次数",
                        EnglishName = "Server Event Trigger Count",
                        GameKey = nameof(ServerEventSettings.SpecialEventTriggetLimitNum),
                        Tooltip = "Max number of times a server event can trigger per day",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                    new ParameterDef
                    {
                        ChineseName = "全服事件开服天数",
                        EnglishName = "Server Event Server Uptime Requirement",
                        GameKey = nameof(ServerEventSettings.SpecialEventServerOpenDay),
                        Tooltip = "Minimum server uptime required to trigger a server event (may differ from default settings).",
                        Min = 0.0,
                        Max = 10.0,
                        Step = 1,
                        Type = ParameterType.Int,
                    },
                }
            },
            new ParameterCategory
            {
                ChineseName = "开关设置",
                EnglishName = "Toggles Settings",
                Params = new List<ParameterDef>
                {
                    new ParameterDef
                    {
                        ChineseName = "ESC菜单允许开启无限建造模式",
                        EnglishName = "Enable Infinite Construction Mode in ESC Menu",
                        GameKey = nameof(ToggleSettings.OpenEscMenuInfJianZao),
                        Tooltip = "Once enabled, an \\",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "濒死功能",
                        EnglishName = "Near-death Function",
                        GameKey = nameof(ToggleSettings.BinSiKaiGuan),
                        Description = "If enabled, damage that would normally kill a player controlled character may instead send them into a near-death state. It is possible to recover from this state by healing and not moving. If disabled, this state is skipped and death happens immediately when HP reaches 0.",
                        Tooltip = "Enable near-death status?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "建筑腐烂",
                        EnglishName = "Building Decay",
                        GameKey = nameof(ToggleSettings.JianZhuFuLanKaiGuan),
                        Description = "If enabled, player buildings that are outside the protection range of a lit bonfire will decay over time. If disabled, player buildings will never decay.",
                        Tooltip = "Enable Construction Decay mechanism?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "保留初始角色成长数据",
                        EnglishName = "Save initial character growth data",
                        GameKey = nameof(ToggleSettings.HuiFuChuShiBodyData),
                        Tooltip = "Save initial physical data?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "完美重塑",
                        EnglishName = "Perfect Remodel",
                        GameKey = nameof(ToggleSettings.WanMeiChongSu),
                        Description = "Whether to enable the “perfect remodel” feature, which allows saved tribesman to be resurrected while retaining their experience and stats.",
                        Tooltip = "When Perfect Remodel is unlocked, the level, Proficiency and Talent stats of the tribesmen last entered will be saved",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "复活后移动死亡包到玩家脚下",
                        EnglishName = "Respawning moves the death package to player position",
                        GameKey = nameof(ToggleSettings.FuHuoMoveSiWangBaoKaiGuan),
                        Description = "If enabled, the items dropped by a player when they die will appear at the location where they respawn. If disabled, the items will drop at the location where the player died.",
                        Tooltip = "Enable Death Pack at player's feet upon revival?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "允许自建传送门运输物资",
                        EnglishName = "Enable custom-built portals to transport resources",
                        GameKey = nameof(ToggleSettings.JianZhuChuanSongMenPlusKaiGuan),
                        Description = "If enabled, players will be allowed to carry items through portals.",
                        Tooltip = "Portal built by players, capable of storing supplies.",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "随机入侵功能",
                        EnglishName = "Random Invasion Function",
                        GameKey = nameof(ToggleSettings.SuiJiRuQinKaiGuan),
                        Description = "Sets the lowest number of enemies that can spawn in an invasion.",
                        Tooltip = "The smaller the value, the lower the Invasion Monster Total Qty Lower Limit.",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "蛮人入侵",
                        EnglishName = "Barbarian Invasion",
                        GameKey = nameof(ToggleSettings.RuQinKaiGuan),
                        Description = "Whether to enable barbarian attacks on player bases.",
                        Tooltip = "Enable Barbarian Invasion (randomly triggers after Heat reaches a critical point)?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "营寨里有玩家时仍然定时刷新敌人",
                        EnglishName = "Enemies respawn regularly in the barracks even when players are inside",
                        GameKey = nameof(ToggleSettings.ShuaXinNPCKaiGuan),
                        Description = "The inhabitants of barbarian barracks and fortresses respawn at midnight each game day. If this setting is disabled, then the respawns will be delayed if there are players inside of the barracks.",
                        Tooltip = "Refresh enemies periodically (after 0:00) in the barracks when players are inside?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "允许打开其他人工作台",
                        EnglishName = "Allow opening of other players' crafting tables",
                        GameKey = nameof(ToggleSettings.YunXuOtherDaKaiGongZuoTai),
                        Description = "If enabled, players can access crafting tables built by other tribes.",
                        Tooltip = "Allow opening other players' Crafting Tables?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "允许打开其他人箱子",
                        EnglishName = "Allow opening of other players' chests",
                        GameKey = nameof(ToggleSettings.YunXuOtherDaKaiXiangZi),
                        Description = "If enabled, players can access storage chests built by other tribes.",
                        Tooltip = "Allow opening other players' chests?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "PVE模式仅本部落玩家可以捡取死亡包",
                        EnglishName = "Only tribe members can pick up death package",
                        GameKey = nameof(ToggleSettings.PVEOnlyTongGuiShuCanOpenKaiGuan),
                        Description = "If enabled, only members of a player’s tribe may collect items they drop on death. If disabled, anyone can collect the items. Setting only works if PVP is disabled.",
                        Tooltip = "Only allow players from your tribe to open death packs in PvE mode？",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "建筑摆放缓慢回血",
                        EnglishName = "Slow HP recovery from placing building",
                        GameKey = nameof(ToggleSettings.KaiQiJianZhuHuiXueBuilding),
                        Description = "If enabled, buildings will be constructed with nearly 0 HJP and will regenerate over time until full. If disabled, buildings will be instantly constructed at full HP.",
                        Tooltip = "Enable New Construction Health Regen mode?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "允许攀爬",
                        EnglishName = "Enable Climbing",
                        GameKey = nameof(ToggleSettings.PanpaKaiGuan),
                        Description = "If enabled, players are able to climb steep slopes and cliffs as well as some walls.",
                        Tooltip = "Enable climbing?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "允许PVP",
                        EnglishName = "Enable PVP",
                        GameKey = nameof(ToggleSettings.HuXIangShangHaiKaiGuan),
                        Description = "If enabled, the game will be set to PVP mode enabling combat between player tribes. Several other settings depend on this setting.",
                        Tooltip = "Enable PVP?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "允许友方玩家之间互相伤害",
                        EnglishName = "Enable friendly fire",
                        GameKey = nameof(ToggleSettings.PlayerYouFangShangHaiKaiGuan),
                        Description = "If enabled, friendly fire between players is possible. Only works if PVP is enabled.",
                        Tooltip = "Enable friendly fire in PvP? (only effective when PvP is enabled)",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "允许友方单位之间互相伤害",
                        EnglishName = "Enable friendly fire",
                        GameKey = nameof(ToggleSettings.YouFangShangHaiKaiGuan),
                        Description = "If enabled, friendly fire is enabled in general. Only works if PVP is enabled.",
                        Tooltip = "Enable friendly fire (only valid in PVP)?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "跨服功能",
                        EnglishName = "Cross-server Mode",
                        GameKey = nameof(ToggleSettings.KaiQiKuaFu),
                        Description = "Only affects a dedicated server that is part of a server cluster. If enabled, players will be allowed to use the portal terminal on the mysterious island to move a character to another server within the cluster.",
                        Tooltip = "Unlock cross-server mode?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "特殊道具受产出倍率加成",
                        EnglishName = "Special Item Yield Multiplier",
                        GameKey = nameof(ToggleSettings.TeShuDaoJuDropXiShuJiaChengKaiGuan),
                        Tooltip = "EU Server Event End Time (UTC)",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "关闭精神消耗",
                        EnglishName = "Disable Morale Cost",
                        GameKey = nameof(ToggleSettings.JingShenNoXiaoHao),
                        Description = "If enabled, tribesman will not lose morale from doing things that would normally decrease morale.",
                        Tooltip = "Once disabled, there will be no morale cost.",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "建筑限高",
                        EnglishName = "Building Height Limit",
                        GameKey = nameof(ToggleSettings.JianZhuGaoDuLimit),
                        Tooltip = "Enable Giant Elephant and Ship Construction Build Range Limits",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "玩家制作时自动调用周围箱子资源",
                        EnglishName = "Auto-use materials from nearby chests during crafting",
                        GameKey = nameof(ToggleSettings.MakeUseAroundRongQiKaiGuan),
                        Description = "If enabled, materials stored in chests within range of the current bonfire can be used for crafting without needing to take them from the chest manually.",
                        Tooltip = "Auto-use materials from nearby chests during crafting?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "建筑受击交互功能限制",
                        EnglishName = "Interaction Limit for Damaged Buildings",
                        GameKey = nameof(ToggleSettings.JianZhuBeDamageLimit),
                        Description = "If enabled, there will be a cooldown after a building takes damage before it can be relocated or repaired.",
                        Tooltip = "Building can't be relocated or repaired for a short time after taking damage.",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "建筑区域数量限制",
                        EnglishName = "Building Region Limit",
                        GameKey = nameof(ToggleSettings.JianZhuAroundNumLimit),
                        Tooltip = "Enable Giant Elephant and Ship Construction Build Range Limits",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "精英或boss动态实力",
                        EnglishName = "Elite or Boss Dynamic Strength",
                        GameKey = nameof(ToggleSettings.DynamicBossStats),
                        Description = "If enabled, the stats of bosses and elites will scale based on the number of players fighting them.",
                        Tooltip = "The combat stats of Elites and Bosses mount up as the number of combatants increases",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "锁定功能开关",
                        EnglishName = "Auto Lock-on Switch",
                        GameKey = nameof(ToggleSettings.SuoDingKaiGuan),
                        Tooltip = "When enabled, adjustments to server event parameters will take effect. (Note: When enabled, the server event will no longer use the game's default event trigger time and related settings.)",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "族人复制开关",
                        EnglishName = "Copy Tribesman On/Off",
                        GameKey = nameof(ToggleSettings.ZuRenFuZhi),
                        Description = "Toggles whether copying tribemen data at the mysterious table is allowed.",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "传送门互通开关",
                        EnglishName = "Two-way Portal On/Off",
                        GameKey = nameof(ToggleSettings.TransDoorInterworkKaiGuan),
                        Description = "Toggles whether personal portals are connected to world portals.",
                        Tooltip = "Open two-way portal?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "战场功能开关",
                        EnglishName = "Battle Mode On/Off",
                        GameKey = nameof(ToggleSettings.WarKaiGuan),
                        Description = "Whether to allow access to the cross-server arena feature",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "部落探险事件",
                        EnglishName = "Tribe Expedition Event",
                        GameKey = nameof(ToggleSettings.TribalExplorationKaiGuan),
                        Description = "Whether to enable Tribe Expedition Squad events",
                        Tooltip = "Enable Tribe Expedition Squad events?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "遗迹探险事件",
                        EnglishName = "Ruins Expedition Event",
                        GameKey = nameof(ToggleSettings.RuinsExplorationKaiGuan),
                        Description = "Whether to enable Plunderer Expedition Squad events",
                        Tooltip = "Enable Plunderer Expedition Squad events?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "部落运输队事件",
                        EnglishName = "Tribe Transport Fleet Event",
                        GameKey = nameof(ToggleSettings.TribalTransportSwitch),
                        Description = "Whether to enable Tribe Transport Squad events",
                        Tooltip = "Enable Tribe Transport Squad events?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "触发特殊事件Boss",
                        EnglishName = "Trigger Special Event Boss",
                        GameKey = nameof(ToggleSettings.SpecialBossSwitch),
                        Tooltip = "Set the server region for the event: 0-AS (including Asia and Oceania), 1-EU (including Europe and Africa), 2-NA (including North and South America) (It only takes effect when the server event adjustment is enabled)",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "遗迹开启箱子事件",
                        EnglishName = "Ruins Chest Opening Event",
                        GameKey = nameof(ToggleSettings.RelicChestEventSwitch),
                        Description = "Whether to enable the chance of encountering a plunderer event when opening chests in Ruins",
                        Tooltip = "Enable the chance of encountering a plunderer event when opening chests in Ruins?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "Boss死亡事件",
                        EnglishName = "Boss's Death Event",
                        GameKey = nameof(ToggleSettings.BossDeathEventSwitch),
                        Description = "Whether to enable the chance of triggering a special event upon a Boss's death (only triggers when Awareness Strength is Lv. 45 or higher)",
                        Tooltip = "Enable the chance of triggering a special event upon the Boss's death? (only triggers when Awareness Strength is Lv. 45 or higher)",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "营火范围内建筑PVP保护",
                        EnglishName = "PvP protection for buildings within the bonfire range",
                        GameKey = nameof(ToggleSettings.ProtectJianZhuInYingHuoSwitch),
                        Tooltip = "Buildings within the bonfire range are immune to damage from other players",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "全服事件调整开关",
                        EnglishName = "Server Event Adjustment Switch",
                        GameKey = nameof(ToggleSettings.SpecialEventConfigSwitch),
                        Description = "Whether to enable the chance of triggering a special event upon a Boss's death (only triggers when Awareness Strength is Lv. 45 or higher)",
                        Tooltip = "Enable the chance of triggering a special event upon the Boss's death? (only triggers when Awareness Strength is Lv. 45 or higher)",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "面甲拟态升阶开关",
                        EnglishName = "Mask Mimicry Tier-up Toggle",
                        GameKey = nameof(ToggleSettings.MaskRepairUpgradeSwitch),
                        Tooltip = "Mask Mimicry tier-up toggle: unlocks level 2 Mimicry Nodes",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "是否开启引导任务",
                        EnglishName = "Enable/Disable Tutorial Quests",
                        GameKey = nameof(ToggleSettings.IsOpenGuideTask),
                        Tooltip = "Turning this off disables tutorial quests",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "可以在地下城中复活",
                        EnglishName = "Allow Resurrection Inside Dungeon",
                        GameKey = nameof(ToggleSettings.DungeonReborn),
                        Tooltip = "Allow Resurrection Inside Dungeon",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "经营模式入侵开关",
                        EnglishName = "Tribe Mode Invasion Toggle",
                        GameKey = nameof(ToggleSettings.ManageModeRuQin),
                        Tooltip = "Invasion Rule Toggle in Tribe Mode",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "是否开启播放BOSS出场动画",
                        EnglishName = "Enable BOSS intro animation?",
                        GameKey = nameof(ToggleSettings.IsPlayBossAppearanceSequence),
                        Tooltip = "Turning this off disables BOSS intro animation",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "玩家死亡不掉落物品开关",
                        EnglishName = "Player Death Item Drop Toggle",
                        GameKey = nameof(ToggleSettings.PlayerDeathCantDropItemKaiGuan),
                        Tooltip = "Enabling it prevents item drops from player inventory upon death",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "禁用滑翔",
                        EnglishName = "Disable Glide",
                        GameKey = nameof(ToggleSettings.BanGlider),
                        Description = "If on, will prevent players from being allowed to use gliders.",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "预建造功能",
                        EnglishName = "Pre-Build Feature",
                        GameKey = nameof(ToggleSettings.JianZhuMirageKaiGuan),
                        Tooltip = "Enable/disable pre-build feature",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "巨象与船只上建筑建造范围限制",
                        EnglishName = "Giant Elephant and Ship Construction Build Range Limits",
                        GameKey = nameof(ToggleSettings.PingTaiBuildRangeLimit),
                        Tooltip = "Enable Giant Elephant and Ship Construction Build Range Limits",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "巨象与船只上建筑建造数量限制",
                        EnglishName = "Giant Elephant and Ship Construction Build Quantity Limits",
                        GameKey = nameof(ToggleSettings.PingTaiJianZhuNumLimit),
                        Tooltip = "Enable Giant Elephant and Ship Construction Build Quantity Limits",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "机器人收服",
                        EnglishName = "Robot Subjugation",
                        GameKey = nameof(ToggleSettings.JiQiChuZhanKaiGuan),
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "族人远程存取开关",
                        EnglishName = "Tribesman Remote Access Toggle",
                        GameKey = nameof(ToggleSettings.ZuRenDirectCunQu),
                        Tooltip = "Tribesman Remote Access Toggle",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "场景禁建区开关",
                        EnglishName = "Scene No-Construction Zone Toggle",
                        GameKey = nameof(ToggleSettings.JinJianQuKaiGuan),
                        Tooltip = "Enable Scene No-Construction Zones?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "忽略己方营火范围内敌方建筑",
                        EnglishName = "Ignore enemy buildings within the allied bonfire range",
                        GameKey = nameof(ToggleSettings.IgnoreEnemyJianZhuInSelfYingHuo),
                        Tooltip = "Ignore enemy buildings within your bonfire range when placing.",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "船只蓝图建造消耗材料开关",
                        EnglishName = "Ship Blueprint Construction Material Consumption Toggle",
                        GameKey = nameof(ToggleSettings.ShipBlueprintBuildConsumeSwitch),
                        Tooltip = "Should ship blueprint construction consume materials?",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "宝箱掉落装备最高品质开关",
                        EnglishName = "Max Quality Chest Loot Toggle",
                        GameKey = nameof(ToggleSettings.ChestDropEquipmentMaxQualitySwitch),
                        Tooltip = "When enabled, all gear dropped from chests will be of the max quality.",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "互动时忽略玩家与镜头之间的可交互件",
                        EnglishName = "Eliminate interaction objects between camera and players",
                        GameKey = nameof(ToggleSettings.HuDongExcludeBetweenCameraCharacter),
                        Description = "When enabled, interactable objects behind the player character will not be able to be interacted with. This prevents such objects from getting on the way when trying to interact with something in front of the character.",
                        Tooltip = "",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "是否使用移动优化",
                        EnglishName = "Movement Optimization",
                        GameKey = nameof(ToggleSettings.MovementYouHua),
                        Description = "On/Off Movement Optimization. Enable to save bandwidth at the cost of slightly less accurate positions being reported to clients for entities that are moving.",
                        Tooltip = "",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "物理优化开关",
                        EnglishName = "Physics Optimization Toggle",
                        GameKey = nameof(ToggleSettings.WuLiYouHuaKaiGuan),
                        Description = "This is believed to control whether entities outside of a certain distance around players (defined by Physical Optimized Distance setting) will run a reduced cost version of physics simulation to save on processing power.",
                        Tooltip = "",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "显示地牢调试路线",
                        EnglishName = "Show/Hide Dungeon Route",
                        GameKey = nameof(ToggleSettings.DrawDebugDungeon),
                        Tooltip = "Show/hide Dungeon Routes",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                        IsEnabled = false,
                    },
                    new ParameterDef
                    {
                        ChineseName = "背包同步优化",
                        EnglishName = "Package Sync Optimization Toggle",
                        GameKey = nameof(ToggleSettings.BagRepOptimizeSwitch),
                        Tooltip = "Package Sync Optimization Toggle",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                    },
                    new ParameterDef
                    {
                        ChineseName = "在重启时强制重生蛮人",
                        EnglishName = "Force Mob Respawn on Restart Toggle",
                        GameKey = nameof(ToggleSettings.RestartGameForceSpawnMonsterSwitch),
                        Tooltip = "Force Mob Respawn on Game Restart Toggle",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                        IsEnabled = false,
                    },
                    new ParameterDef
                    {
                        ChineseName = "舰队入侵开关",
                        EnglishName = "Fleet Invasion Toggle",
                        GameKey = nameof(ToggleSettings.JianDuiRuQinKaiGuan),
                        Description = "Fleet Invasion Toggle",
                        Tooltip = "",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                        IsEnabled = false,
                    },
                    new ParameterDef
                    {
                        ChineseName = "启用平台对路径规划的影响",
                        EnglishName = "Enable Platform Navigation Affect",
                        GameKey = nameof(ToggleSettings.PingTaiAffectNavigation),
                        Tooltip = "Determines whether platforms affect NPC and creature navigation/pathfinding.",
                        Min = 0,
                        Max = 1,
                        Step = 1,
                        Type = ParameterType.Bool,
                        IsEnabled = false,
                    },
                }
            },
        };
    }

    public class SoulmaskCoefficientConfigCollection
    {
        [JsonPropertyName("0")]
        public SoulmaskCoefficientSettings Default { get; set; } = new SoulmaskCoefficientSettings();
        [JsonPropertyName("1")]
        public SoulmaskCoefficientSettings _0 { get; set; } = new SoulmaskCoefficientSettings();
        [JsonPropertyName("2")]
        public SoulmaskCoefficientSettings _1 { get; set; } = new SoulmaskCoefficientSettings();
    }

    public class Rcon : PropertyChangedBase
    {
        public string IP { get; set; } = "";
        public int Port { get; set; } = 19000;
        public string Password { get; set; } = "";
    }

    public class API
    {
        public bool Enabled { get; set; } = false;
        public int BindPort { get; set; } = 18888;
    }

    public class PresetItem
    {
        public string DisplayName { get; set; }
        public string FileName { get; set; }
    }

    public class ServerSettings
    {
        public string SteamServerName { get; set; } = "My Soulmask Server";
        public int Port { get; set; } = 8777;
        public int QueryPort { get; set; } = 27015;
        public int EchoPort { get; set; } = 18888;
        public int MaxPlayers { get; set; } = 40;
        public string Password { get; set; } = "";
        public string GMPassword { get; set; } = "";
        //public bool PVP { get; set; } = false;
        public int Backup { get; set; } = 900;
        public int Saving { get; set; } = 600;
        public string Map { get; set; } = "Level01_Main";
        public int AutoSaveCount { get; set; } = 5;
        public int AutoSaveInterval { get; set; } = 5;
        public int AutoCleanInterval { get; set; } = 10;
        public int ClusterMode { get; set; } = 0;
        public string PublicIP { get; set; } = "";
        public bool UseManualIP { get; set; } = false;
        public bool UseManualMainPort { get; set; } = false;
        public int MainPort { get; set; } = 0;
        public int ManualMainPort { get; set; } = 0;
        public int ServerId { get; set; } = 0;
        public string SelfServerUniqueId { get; set; } = "";
        public string MainServerUniqueId { get; set; } = "";
        public string ServerPresetSettings { get; set; } = "无";
        public string Mods { get; set; } = "";
        public Rcon Rcon { get; set; } = new Rcon();
    }
}
