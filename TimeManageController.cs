using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public delegate void TimeChangeEvent(GameTimeState gameTimeState,double _curHour, double _curMin);
public delegate void BuildFixEvent(BuildingState buildState,int comm_id,int build_id);

public class TimeManageController : MonoBehaviour 
{
    #region 变量声明
    public double Global_TimeMin, Global_GameDay, Global_GameHour, Global_GameDaysPassed;
    public double Global_TimeSec;
    public float _TimeScale, _TimeScale_baifen;
    [Header("游戏时间比例-->单位分钟")]
    public int GTProportion = 15;
    public int OneDay_in_sec = 43200;//游戏内一天12小时是43200秒
    public bool testTouch = false;

    // 因为核心是游戏内时间，如果时间暂停会直接暂停收益，所以玩家数据暂时都放在时间管理类之中
    public User CurrentUserData;
    private double lastQueryHour = -1;
    private double lastQueryMin = -1;
    //控制游戏时间是否暂停标签
    private bool isPeause = true;
    private MainUIWnd mainuiWnd;
    private bool isNextDay = false;
    private GameObject CommObj;

    public float TimeSpeedup = 1.0f;//加速倍率
    //游戏时间暂停的广播
    public event TimeChangeEvent onTimeStateChange;
    public event BuildFixEvent onBuildFixEventChange;

    public List<UserProdutionunitDAO> _currentALLUserProdutionunitData;//当前玩家选择的小区全部建筑数据
    public List<UserReviewEventDAO> _DailyReviewList;
    public List<UserReviewEventDAO> _DailyEmergencyList;// 数据层的逻辑几乎相同，突发的入口改成在大地图点击居民模型，所以此处复用相同数据结构
    public RedDotDAO redDotDAO;
    public BaseTimeConfigDAO baseTimeConfigDAO;
    #endregion

    public static TimeManageController Instance { get; private set; }

    public void StartWithDBComplete() 
    {

    }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }

        redDotDAO = new RedDotDAO();

        CurrentUserData = UserDataTool.Instance.FindUserData();

        if (CurrentUserData.user_seconds > 0)
            Global_TimeSec = CurrentUserData.user_seconds;

        baseTimeConfigDAO = BaseTimeConfigDataTool.Instance.FindById(1);
        GTProportion = baseTimeConfigDAO.basic_time;

        _TimeScale = 43200 / (GTProportion * 60);//秒级单位
        _TimeScale_baifen = 12 * 60 * 0.01f;
        //初始化给时间+6，因为游戏内一天是6：00-18:00
        Global_GameHour = (((Global_TimeSec / (60 * 60)) % 12) + 6);
        Global_GameDaysPassed = ((int)Global_TimeSec / OneDay_in_sec);
        if (Global_GameDaysPassed >= 1)
            Global_TimeSec = Global_TimeSec - (OneDay_in_sec*(int)Global_GameDaysPassed);

        // 进入游戏初始化当前的小区建筑物状态，读取当前小区建筑物数据
        _currentALLUserProdutionunitData = UserProductionunitDataTool.Instance.FindALL();

        _DailyReviewList = UserReviewEventDataTool.Instance.FindAllReview();
        _DailyEmergencyList = UserReviewEventDataTool.Instance.FindAllEmergency();

        DontDestroyOnLoad(this);

    }

    //部分读库的操作是在start执行，避免读写冲突，执行顺序出问题
    private void Start()
    {
        //游戏第一天特殊处理下，因为所有事件的逻辑都在当天结算时候才会触发，所以第一天要刚进游戏就触发一次第一天的数据才会有
        if (Global_GameDaysPassed <= 0 && Global_GameHour<=6)
        {
            BaseEventCategoryDAO b = BaseEventCategoryDataTool.Instance.FindByGameDay(1, 1);
            UpdateDailyFixEvent(b);
            UpdateDailyReviewEvent(b);
            UpdateDailyEmergency(b);

        }
        mainuiWnd = GameObject.FindGameObjectWithTag("MainUIOnly").GetComponent<MainUIWnd>();
        CommObj = GameObject.Find("Comm001(Clone)");
    }

    const float DAY_LENGTH = 12 * 60f * 60f;
    const float HOUR_LENGTH = 60 * 60;

    private float lastCheckGameTimeMin = 0f;

    void FixedUpdate()
    {
        if (isPeause) 
        {
            Global_TimeSec += (Time.deltaTime * TimeSpeedup) * _TimeScale;
            Global_GameDay = (Global_TimeSec / DAY_LENGTH);
            Global_GameHour = (((Global_TimeSec / HOUR_LENGTH) % 12)+6);
            Global_TimeMin = ((Global_TimeSec / 60) % 60);

            double _tmeMin = System.Math.Floor(Global_TimeMin);
            double _tmpHour = System.Math.Floor(Global_GameHour);
            double _tmpCKMin = System.Math.Floor(lastCheckGameTimeMin);

            // 当游戏时间到达18:00时，重置为6:00并触发事件,此处逻辑和每小时检查不一样，此处逻辑是让时针重置，并且在每天17:59时进入检查
            if (Global_GameHour >= 17.99)
            {
                isNextDay = true;
                Global_TimeSec = 0;
                Global_GameDaysPassed++;  // 增加游戏内经过的天数
                OnDayEnd(); // 这是一个新方法，用于处理每天结束时的事件
            }

            if (_tmeMin != lastQueryMin)
            {
                if (lastQueryMin > 0 && mainuiWnd!=null) // 大退之后进入游戏会导致过快的加载，其他类的start(),awake()都没执行完，所以第一帧先空过去
                {
                    mainuiWnd.RefreshClock(_tmeMin,_tmpHour);
                }
                lastQueryMin = _tmeMin;
            }

            // 每5分钟判断一次
            if (_tmeMin >= _tmpCKMin + 5 ||
            (_tmeMin < _tmpCKMin &&
             _tmeMin + 60 >= _tmpCKMin + 5))
            {
                //Debug.Log($"5.MIN: {_tmeMin}   -->   last check min: {_tmpCKMin}");
                lastCheckGameTimeMin = (float)System.Math.Floor(Global_TimeMin);
                StartCoroutine(TimeCheckOnMin());
                StartCoroutine(CheckCitizenLevel());
                StartCoroutine(CheckALLBuildNeedLevelup());
                StartCoroutine(CheckEmployeLevelup());
                
            }

        }        
    }

    // 一天结束的检查事件
    void OnDayEnd()
    {
        GameTimePeause();
        int cur_chatper_days = 0;
        if (CurrentUserData.unlock_new_community_num == 1) 
        {
            CurrentUserData.chapter_one_days += 1;
            cur_chatper_days = CurrentUserData.chapter_one_days;
        }
        if (CurrentUserData.unlock_new_community_num == 2) 
        {
            CurrentUserData.chapter_two_days += 1;
            cur_chatper_days = CurrentUserData.chapter_two_days;
        }
        if (CurrentUserData.unlock_new_community_num == 3) 
        {
            CurrentUserData.chapter_three_days += 1;
            cur_chatper_days = CurrentUserData.chapter_three_days;
        }
        //每天结算时刻，查询下一天的修理任务，Global_GameDaysPassed在此时已经++变成下一天
        BaseEventCategoryDAO b = BaseEventCategoryDataTool.Instance.FindByGameDay(cur_chatper_days, CurrentUserData.unlock_new_community_num);

        //清除当日地图上没有做的突发事件
        for (int i=0;i< CommObj.GetComponent<Community1Controller>().emergencyRoleList.Count;i++) 
        {
            if (CommObj.GetComponent<Community1Controller>().emergencyRoleList[i] != null) 
            {
                UserReviewEventDataTool.Instance.UpdateUserReview(CommObj.GetComponent<Community1Controller>().emergencyRoleList[i].GetComponent<CitizenRoleHandler>().GetEventId()); // 需要先更新user_review当日的突发事件数据
                Destroy(CommObj.GetComponent<Community1Controller>().emergencyRoleList[i]);
            }
        }

        // 每天结算时间更新修理事件库
        UpdateDailyFixEvent(b);
        UpdateDailyReviewEvent(b);
        UpdateDailyEmergency(b);
    }

    // 更新每天的修理事件
    void UpdateDailyFixEvent(BaseEventCategoryDAO becDAO) 
    {
        if (becDAO != null)
        {
            #region 修理任务更新
            string[] fixTaskArray = DisassembleEventTask(becDAO.fix_event_id);
            List<GameTimeStruct> CurrentDayilyFixEventList = DisassembleEventTime(becDAO.fix_event_time);
            for (int j = 0; j < CurrentDayilyFixEventList.Count; j++)
            {
                SingleFixEventDAO _single = new SingleFixEventDAO();
                if (becDAO.fix_random != 0) // 不随机抽取
                {
                    if (fixTaskArray[j].Equals(""))
                        break;
                    _single.fix_event_id = fixTaskArray[j];

                }
                else// 随机抽取事件 
                {
                    int _index = Random.Range(0, fixTaskArray.Length);
                    string _tmp = fixTaskArray[_index];
                    fixTaskArray[_index].Remove(_index);
                    _single.fix_event_id = _tmp;

                }

                _single.hour = (CurrentDayilyFixEventList[j].GameHours - 6) * 60 ;
                
                //_single.min = CurrentDayilyFixEventList[i].GameMinutes + _single.hour;
                _single.triger_time = _single.hour + CurrentDayilyFixEventList[j].GameMinutes + 360;
                BaseFixLibraryDAO bflDAO = BaseFixLibraryDataTool.Instance.FindByFixEventID(int.Parse(_single.fix_event_id));

                //DailyFixEventList.Add(_single);
                // 按照最新地图每个建筑物遍历，将维修事件赋值到单个建筑物身上，同一个建筑物一天可能会有多次维修事件触发
                for (int k=0; k < _currentALLUserProdutionunitData.Count; k++) 
                {
                    if (bflDAO.community_id == _currentALLUserProdutionunitData[k].community_num && 
                        _currentALLUserProdutionunitData[k].commmunity_build_num == bflDAO.build_id && 
                        _currentALLUserProdutionunitData[k].buildingSpecialEventDAO.buildingState == BuildingState.BuildingRunning) 
                    {
                        //此处需要赋值是因为，用户有可能到达了触发时间之后，建筑物状态变更，可是用户并没有进行修理就大退游戏，所以需要存储，等用户再进游戏，需要用此event_id去查找对应事件数据
                        _currentALLUserProdutionunitData[k].buildingSpecialEventDAO.eventId =int.Parse(_single.fix_event_id);

                        BuildingSingleFixEventDAO bsfeDAO = new BuildingSingleFixEventDAO();
                        bsfeDAO.build_id = bflDAO.build_id;
                        bsfeDAO.community_id = bflDAO.community_id;
                        bsfeDAO.fix_time = bflDAO.fix_time;
                        bsfeDAO.triger_time = _single.triger_time;
                        bsfeDAO.bonus_type = bflDAO.bonus_type;
                        bsfeDAO.alert_string = bflDAO.alert_string;
                        _currentALLUserProdutionunitData[k].singleFixEventDAO =bsfeDAO;
                        Debug.Log($"建筑物:{_currentALLUserProdutionunitData[k].singleFixEventDAO.build_id}   " +
                            $"修理事件{_currentALLUserProdutionunitData[k].singleFixEventDAO.build_id }  " +
                            $"预计触发时间与：{_currentALLUserProdutionunitData[k].singleFixEventDAO.triger_time} 分钟后启动   " +
                            $"该修理事件所需时间：{_currentALLUserProdutionunitData[k].singleFixEventDAO.fix_time}");
                    }
                }
            }
            #endregion
        }
        else
        {
            Debug.Log($"没任务了");
        }
    }
    // 更新每天突击事件
    void UpdateDailyEmergency(BaseEventCategoryDAO becDAO) 
    {
        if (UserReviewEventDataTool.Instance.CheckUserEmergencyCount() > 0)
        {
            _DailyEmergencyList = UserReviewEventDataTool.Instance.FindAllEmergency();
        }
        else 
        {
            if (becDAO != null)
            {
                if (_DailyEmergencyList != null)
                    _DailyEmergencyList.Clear();
                else
                    _DailyEmergencyList = new List<UserReviewEventDAO>();

                string[] emerg_id = DisassembleEventTask(becDAO.emergency_id);
                List<GameTimeStruct> CurrentDayilyEmergencyList = DisassembleEventTime(becDAO.emergency_event_time);
                for (int i = 0; i < CurrentDayilyEmergencyList.Count; i++)
                {
                    SingleEmergencyDAO _single = new SingleEmergencyDAO();

                    _single.hour = (CurrentDayilyEmergencyList[i].GameHours - 6) * 60;
                    _single.triger_time = _single.hour + CurrentDayilyEmergencyList[i].GameMinutes + 360;
                    _single.emergency_role_spawn = this.DisassembleEmergencyRoleId(emerg_id[i]).emergency_role_spawn;
                    _single.emergency_role_id = this.DisassembleEmergencyRoleId(emerg_id[i]).emergency_role_id;
                    _single.emergency_id = this.DisassembleEmergencyRoleId(emerg_id[i]).emergency_id;
                    BasePlotCategoryDAO bpcDAO = BasePlotCategoryDataTool.Instance.FindplotcategoryByEventId(int.Parse(_single.emergency_id));

                    UserReviewEventDAO ureDAO = new UserReviewEventDAO();
                    ureDAO.review_or_emergency = 2; // 因数据结构相同，所以靠此标签进行判断，标签含义详情见UserReviewEventDAO
                    ureDAO.review_event_id = int.Parse(_single.emergency_id);
                    ureDAO.review_event_time = _single.triger_time;
                    ureDAO.event_name = _single.emergency_role_spawn;
                    ureDAO.event_context = _single.emergency_role_id;
                    ureDAO.is_stop_time = 0;// 突发事件永远不停止时间
                    ureDAO.comm_id = 1; //  因为已经有了居民模型id所以此处并不依赖小区id

                    //此时应该插入数据
                    UserReviewEventDataTool.Instance.AddUserReview(ureDAO);
                    _DailyEmergencyList.Add(ureDAO);
                    Debug.Log($"突发事件:{i}号 --> 触发时间:{_DailyEmergencyList[i].review_event_time}后 --> 事件名称:{_DailyEmergencyList[i].event_name}");
                }
            }
            else
            {
                Debug.Log("当日没有突发事件");
            }
        }
    }

    // 更新每天的审核事件
    void UpdateDailyReviewEvent(BaseEventCategoryDAO becDAO) 
    {
        #region 审核任务更新 需要检查user_review，如果有数据就不需要检查，直接从user_review读取数据
        if (UserReviewEventDataTool.Instance.CheckUserReviewCount() > 0)
        {
            _DailyReviewList = UserReviewEventDataTool.Instance.FindAllReview();
        }
        else 
        {
            string[] reviewTaskArray = DisassembleEventTask(becDAO.review_id);
            List<GameTimeStruct> CurrentDailyReviewList = DisassembleEventTime(becDAO.review_event_time);
            if (_DailyReviewList != null)
                _DailyReviewList.Clear();
            else
                _DailyReviewList = new List<UserReviewEventDAO>();

            for (int i = 0; i < CurrentDailyReviewList.Count; i++)
            {
                if (reviewTaskArray[i].Equals(""))
                    break;
                SingleReviewEventDAO _singleReview = new SingleReviewEventDAO();
                _singleReview.review_event_id = reviewTaskArray[i];

                _singleReview.hour = (CurrentDailyReviewList[i].GameHours - 6) * 60;
                _singleReview.triger_time = _singleReview.hour + CurrentDailyReviewList[i].GameMinutes + 360;
                BasePlotCategoryDAO bpcDAO = BasePlotCategoryDataTool.Instance.FindplotcategoryByEventId(int.Parse(_singleReview.review_event_id));

                UserReviewEventDAO ureDAO = new UserReviewEventDAO();
                ureDAO.review_or_emergency = 1;
                ureDAO.review_event_id = bpcDAO.event_id;
                ureDAO.review_event_time = _singleReview.triger_time;
                ureDAO.event_name = bpcDAO.event_name;
                ureDAO.event_context = bpcDAO.event_context;
                ureDAO.is_stop_time = bpcDAO.is_stop_time;
                ureDAO.comm_id = bpcDAO.comm_id;
                ureDAO.need_fightready = bpcDAO.need_fightready;

                //此时应该插入数据
                UserReviewEventDataTool.Instance.AddUserReview(ureDAO);
                _DailyReviewList.Add(ureDAO);
                Debug.Log($"审核事件:{i}号 --> 触发时间:{_DailyReviewList[i].review_event_time}后 --> 事件名称:{_DailyReviewList[i].event_name}");
            }
        }
        #endregion
    }

    // 各种任务id的数据拆分，数据格式以;为分隔符，并且最后一条数据也要加上分号
    public string[] DisassembleEventTask(string _source) 
    {
        string[] _tmpa = _source.Split(';');
        
        return _tmpa;
    }

    public void GoldChangeAdd(int _gold) 
    {
        if (MainQuestTotalDataTool.Instance != null && mainuiWnd!=null) 
        {
            //Debug.Log($"time.gold.add:{_gold}");
            QuestQueryResult qqr = MainQuestTotalDataTool.Instance.IncrementCompleteNum((int)MainQuestEnum.累计获得X金币,_gold);
            mainuiWnd.RefreshMainQuest((int)MainQuestEnum.累计获得X金币, qqr.res_count);
        }
    }

    public void GoldChangeDecrease(int _gold) 
    {
        if (MainQuestTotalDataTool.Instance != null && mainuiWnd != null) 
        {
            //Debug.Log($"time.gold.decr:{_gold}");
            QuestQueryResult qqr = MainQuestTotalDataTool.Instance.IncrementCompleteNum((int)MainQuestEnum.累计消耗X金币,_gold);
            mainuiWnd.RefreshMainQuest((int)MainQuestEnum.累计消耗X金币, qqr.res_count);
        }
    }

    // 任务时间点数据拆箱
    public List<GameTimeStruct> DisassembleEventTime(string _source) 
    {
        List<GameTimeStruct> eventTrigerTime = new List<GameTimeStruct>();
        string _tmps = _source;
        string[] _tmpa = _tmps.Split(';');
        for (int i = 0; i < _tmpa.Length; i++)
        {
            if (_tmpa[i].Equals(""))
                break;
            string[] _tmpC = _tmpa[i].Split(':');
            GameTimeStruct gts = new GameTimeStruct();
            gts.GameHours = int.Parse(_tmpC[0]);
            gts.GameMinutes = int.Parse(_tmpC[1]);
            eventTrigerTime.Add(gts);
        }
        return eventTrigerTime;
    }


    //游戏内每分钟检查各种事件
    IEnumerator TimeCheckOnMin() 

    {   // 每分钟检查建筑物特殊事件状态等
        if (_currentALLUserProdutionunitData.Count > 0)
        {
            for (int i = 0; i < _currentALLUserProdutionunitData.Count; i++)
            {
               
                //首先检查各个建筑物当前事件状态，维修和特殊升级为互斥,如果是正常运行，即可以触发维修状态
                if (_currentALLUserProdutionunitData[i].buildingSpecialEventDAO.buildingState == BuildingState.BuildingRunning) 
                {
                   
                    if (_currentALLUserProdutionunitData[i].singleFixEventDAO.triger_time >0 &&
                        _currentALLUserProdutionunitData[i].singleFixEventDAO!=null && (GetDailyTotalMin()) >= _currentALLUserProdutionunitData[i].singleFixEventDAO.triger_time) 
                    {
                        _currentALLUserProdutionunitData[i].buildingSpecialEventDAO.buildingState = BuildingState.BuildingBreak;

                        onBuildFixEventChange(_currentALLUserProdutionunitData[i].buildingSpecialEventDAO.buildingState, _currentALLUserProdutionunitData[i].community_num, _currentALLUserProdutionunitData[i].commmunity_build_num);
                        Debug.Log($"建筑物{_currentALLUserProdutionunitData[i].commmunity_build_num} 坏了！！   当前累计分钟数:{(GetDailyTotalMin())}    预计触发时间:{_currentALLUserProdutionunitData[i].singleFixEventDAO.triger_time}");
                    }
                }
                //如果该建筑物一直没有修理，并且当天又有了改建物修理事件
                //if (_currentALLUserProdutionunitData[i].buildingSpecialEventDAO.buildingState == BuildingState.BuildingBreak)
                //{
                //}

                //建筑物正在升级中，不触发修理事件，并且删掉该修理事件
                if (_currentALLUserProdutionunitData[i].buildingSpecialEventDAO.buildingState == BuildingState.BuildingSpecialUpdating)
                {
                    if (GetCurrentSec() >= _currentALLUserProdutionunitData[i].buildingSpecialEventDAO.EndingTime)
                    {
                        //TODO:需要给其他界面或者GameObj一个通知，具体哪些，需要仔细 暂定 大地图会有特效，UI屏幕会有toast提示
                        //Debug.Log($"-------------------------第{BuildSpecialList[i]}号建筑大升级结束啦!");
                    }
                    if (_currentALLUserProdutionunitData[i].singleFixEventDAO !=null && (GetDailyTotalMin()) >= _currentALLUserProdutionunitData[i].singleFixEventDAO.triger_time) 
                    {
                        // 不触发该事件，并且删掉该事件
                        //_currentALLUserProdutionunitData[i].singleFixEventDAO = null;
                    }
                }
            }
        }
        // 每分钟检查审核任务
        if (_DailyReviewList!=null && _DailyReviewList.Count > 0) 
        {
            for (int i=0;i<_DailyReviewList.Count;i++) 
            {
                if (GetDailyTotalMin() >= _DailyReviewList[i].review_event_time) 
                {
                    Debug.Log($"今天第{i}个 审核任务:{_DailyReviewList[i].event_name}");
                    mainuiWnd.OnReviewEventActive(_DailyReviewList[i]);
                    _DailyReviewList.RemoveAt(i);
                }
            }
        }
        // 每分钟检查突击任务
        if (_DailyEmergencyList != null && _DailyEmergencyList.Count > 0)
        {
            for (int i = 0; i < _DailyEmergencyList.Count; i++)
            {
                if (GetDailyTotalMin() >= _DailyEmergencyList[i].review_event_time)
                {
                    Debug.Log($"今天第{i}个 突击任务:{_DailyEmergencyList[i].event_name}");

                    if (CommObj != null)
                    {
                        Community1Controller comm1 = CommObj.GetComponent<Community1Controller>();
                        comm1.CreateEmergencyRole(_DailyEmergencyList[i].event_context, _DailyEmergencyList[i].event_name, _DailyEmergencyList[i].review_event_id.ToString());
                        _DailyEmergencyList.RemoveAt(i);
                    }
                    else 
                    {
                        CommObj = GameObject.Find("Comm001(Clone)");
                    }
                    
                }
            }
        }
        yield return null;
    }


    //暂停游戏时间
    public void GameTimePeause() 
    {
        isPeause = false;
        onTimeStateChange(GameTimeState.GameTimePeause, Global_GameHour, Global_TimeMin);
    }

    //恢复游戏时间
    public void GameTimeReStart() 
    {
        isPeause = true;
        onTimeStateChange(GameTimeState.GameTimeRunning, Global_GameHour, Global_TimeMin);
    }

    // 更新物业公司的经验
    public void UserEXPUpdate(int exp) 
    {
        CurrentUserData.user_exp += exp;
        //CurrentUserData.user_seconds = 
        mainuiWnd.RefreshLvAndEXP();

        BaseCompanyMoreInfoDAO bcmi = BaseCompanyDataTool.Instance.FindNextLevel(CurrentUserData.user_level);
        if (CurrentUserData.user_exp >= bcmi.baseCompanyDAO.level_exp) {
            mainuiWnd.OnCompanyLevelupBtnClick();
            mainuiWnd.RefreshLvAndEXP();
            Debug.Log("下一级所需经验已经到位");
        }
    }

    public void UpdateUserCitizen(int _favor,int _cid) 
    {
        //检查下当前这个居民的好感度是否可以领取物业公司商誉了
        UserCitizenDAO ucDAO = UserCitizenDataTool.Instance.FindByCId(_cid);
        BaseCitizenDAO bcDAO = BaseCitizenDataTool.Instance.FindById(_cid);

        int _res = _favor + ucDAO.current_favorability;
        UserCitizenDataTool.Instance.UpdateCurrentFavorability(_res, _cid);

        QuestQueryResult qqr = MainQuestTotalDataTool.Instance.IncrementCompleteNum((int)MainQuestEnum.X个NPC好感度达到X);
        mainuiWnd.RefreshMainQuest((int)MainQuestEnum.X个NPC好感度达到X, qqr.res_count);

        //List<string> citizenFavorList = BaseCitizenDataTool.Instance.ParseCompanyExForFavorability(bcDAO.companyex_for_favorability);
        //for (int i=0;i < citizenFavorList.Count;i++) 
        //{
        //    if (ucDAO.current_favorability >= int.Parse(citizenFavorList[i])) 
        //    {
        //        //证明当前好感度可以领取两个商誉中的某一个，还要去找对应的是否领取过，没领去过就去做红点提醒
        //        if (i == 0 && ucDAO.current_companyex_1 == 0)
        //            Debug.Log($"第{_cid} 号居民的第一阶商誉可以领取了");
        //        if (i == 1 && ucDAO.current_companyex_1 == 0)
        //            Debug.Log($"第{_cid} 号居民的第二阶商誉可以领取了");
        //    }
        //}
    }

    public IEnumerator CheckCitizenLevel() 
    {
        List<UserCitizenDAO> allUCList = UserCitizenDataTool.Instance.FindAllCitizens();
        List<BaseCitizenDAO> allBCList = BaseCitizenDataTool.Instance.FindAllCitizens();

        bool isTotalRedDot = false;// mainui那个按钮的红点判断
        bool isTotalPointDot = false;// mainui那个可以左右上下滑动面板里，村民头像的那个红点判断
        
        for (int i=0;i<allUCList.Count;i++) 
        {
            List<string> _cFavorList = BaseCitizenDataTool.Instance.ParseCompanyExForFavorability(allBCList[i].companyex_for_favorability);
            for (int j=0;j<_cFavorList.Count;j++) 
            {
                if (allUCList[i].current_favorability >= int.Parse(_cFavorList[j]))
                {
                    if (j == 0 && allUCList[i].current_companyex_1 == 0)
                    {
                        //Debug.Log($"第{allUCList[i].cid} 号居民 {allBCList[i].name} 的第一阶商誉可以领取了");
                         isTotalPointDot = true;
                         isTotalRedDot = true;
                    }

                    if (allUCList[i].current_companyex_1 == 1  ) // 玩家好感度没达到2号领取，但是已经领取了1号，就不用弹
                    {
                        isTotalPointDot = false;
                    }

                    if (j == 1 && allUCList[i].current_companyex_2 == 0)
                    {
                       // Debug.Log($"第{allUCList[i].cid} 号居民 {allBCList[i].name} 的第二阶商誉可以领取了");
                        isTotalPointDot = true;
                        isTotalRedDot = true;
                    }

                    if (allUCList[i].current_companyex_1 == 1 && allUCList[i].current_companyex_2 == 1  ) // 这种判断是为了预防玩家只领了2号商誉，没领1号商誉，依旧要弹红点
                    {
                        isTotalPointDot = false;
                    }

                }
            }

            if (isTotalPointDot && redDotDAO.CitizenRDObjList.Count > 0 && i < redDotDAO.CitizenRDObjList.Count)
            {
                redDotDAO.CitizenRDObjList[i].SetActive(true);
            }

            if (!isTotalPointDot && redDotDAO.CitizenRDObjList.Count > 0 && i < redDotDAO.CitizenRDObjList.Count)
            {
                redDotDAO.CitizenRDObjList[i].SetActive(false);
            }
        }
        
        if (!isTotalRedDot) // 此处证明确实没有可以提示红点的
        {
            redDotDAO.JuminBtn.SetActive(false);
        }
        if (isTotalRedDot) 
        {
            redDotDAO.JuminBtn.SetActive(true);
        }
        yield return null;
    }

    //所有的红点我统一命名为reddot所以可以直接封装好
    private GameObject FindRedDot(GameObject _parentObj) 
    {
        return _parentObj.transform.Find("reddot").gameObject;
    }

    // 检查全部建筑物是否需要升级，提示红点用,主界面下面三个按钮里那个建筑的按钮
    public IEnumerator CheckALLBuildNeedLevelup()
    {
        List<UserProdutionunitDAO> listUP = UserProductionunitDataTool.Instance.FindByCommId(CurrentUserData.user_community_num);
        bool totalHasLvUp = false;
        for (int i = 0; i < listUP.Count; i++)
        {
            //Debug.Log($"-------------------------------->当前检查建筑物第{listUP[i].commmunity_build_num}号<------------------------------------------");

            bool singleLvUp = CheckBuildLevelup(listUP[i].commmunity_build_num);
            if (singleLvUp) 
            {
                totalHasLvUp = true;
                break; //只要有一个有了就行，就意味着按钮红点必须显示
            }
        }
        yield return null;

        if (totalHasLvUp)
            redDotDAO.SheshiBtn.SetActive(true);
        else
            redDotDAO.SheshiBtn.SetActive(false);
    }


    public IEnumerator CheckALLBuildNeedLevelupForUI() 
    {
        List<UserProdutionunitDAO> listUP = UserProductionunitDataTool.Instance.FindByCommId(CurrentUserData.user_community_num);
        for (int i = 0; i < listUP.Count; i++)
        {
            CheckBuildLevelup(listUP[i].commmunity_build_num);
            yield return null;
        }
    }

    // 检查单个建筑物是否需要升级
    public bool CheckBuildLevelup(int _buidId)
    {
        int curCommunity = CurrentUserData.user_community_num;
        UserProdutionunitDAO updao = UserProductionunitDataTool.Instance.FindProductionunityByNumandCom(_buidId, curCommunity);
        BaseProdutionMoreInfoDAO nextBaseDao = BaseProdutionunitDataTool.Instance.FindProductionNextLevel(curCommunity, _buidId, updao.level);

        if (nextBaseDao != null)//空了就代表建筑物升满级了
        {
            bool isMoneyEnough = CurrentUserData.user_gold >= nextBaseDao.baseProDao.levelmoney;
            bool areOtherBuildingsReady = true;
            bool isCompanyLevelEnough = true;

            if (nextBaseDao.baseProDao.isSpecial == 1) //大升级，检查多种条件 
            {
                for (int i = 0; i < nextBaseDao.special_list.Count; i++)
                {
                    if (nextBaseDao.special_list[i].type == 1)//检查其他建筑物的等级 
                    {
                        int res_num = UserProductionunitDataTool.Instance.CheckCommunityProLevelAverage(nextBaseDao.special_list[i].comm_id, nextBaseDao.special_list[i].level, CurrentUserData.user_community_num);
                        if (res_num > 0)
                        {
                            areOtherBuildingsReady = false;
                            break; // 一旦找到不满足条件的建筑，退出循环
                        }
                    }
                    if (nextBaseDao.special_list[i].type == 2)//检查物业公司等级
                    {
                        if (CurrentUserData.user_level <= nextBaseDao.special_list[i].comm_id)
                        {
                            isCompanyLevelEnough = false;
                            break; // 一旦公司等级不足，退出循环
                        }
                    }
                }
            }

            if ((nextBaseDao.baseProDao.isSpecial != 1 && isMoneyEnough) ||
            (nextBaseDao.baseProDao.isSpecial == 1 && isMoneyEnough && areOtherBuildingsReady && isCompanyLevelEnough))
            {
                // 大地图建筑的箭头，还有mainui里的scroll那个cell的红点
                if (redDotDAO.ProductionRDObjList.Count > 0 && (_buidId - 1) < redDotDAO.ProductionRDObjList.Count)
                {
                    if (!redDotDAO.ProductionRDObjList[_buidId - 1].activeSelf) // 为了防止数组越界异常只能先这么做
                    {
                        redDotDAO.ProductionRDObjList[_buidId - 1].SetActive(true);
                    }
                }
                if (redDotDAO.MapbuildingList.Count > 0 && (_buidId - 1) < redDotDAO.MapbuildingList.Count)
                {
                    if (!redDotDAO.MapbuildingList[_buidId - 1].activeSelf)
                    {
                        redDotDAO.MapbuildingList[_buidId - 1].SetActive(true);
                    }
                }
                return true;
            }
            else
            {
                // 隐藏红点
                if (redDotDAO.ProductionRDObjList.Count > 0 && redDotDAO.ProductionRDObjList[_buidId - 1].activeSelf)
                {
                    redDotDAO.ProductionRDObjList[_buidId - 1].SetActive(false);
                }
                if (redDotDAO.MapbuildingList.Count > 0 && redDotDAO.MapbuildingList[_buidId - 1].activeSelf)
                {
                    redDotDAO.MapbuildingList[_buidId - 1].SetActive(false);
                }
                return false;
            }
        }
        else
        {
            this.ShowPromotBox("当前建筑物以满级，请静待我们后续更新呦");
            return false;
        }
    }


    // 检查单个建筑物是否需要升级
    public List<SpecialEventFailResult> CheckBuildLevelupForBuildBtn(int _buidId)
    {
        int curCommunity = CurrentUserData.user_community_num;
        UserProdutionunitDAO updao = UserProductionunitDataTool.Instance.FindProductionunityByNumandCom(_buidId, curCommunity);
        BaseProdutionMoreInfoDAO nextBaseDao = BaseProdutionunitDataTool.Instance.FindProductionNextLevel(curCommunity, _buidId, updao.level);
        //Debug.Log($"CheckBuildLevelup.updao:{CurrentUserData.user_gold}");
        if (nextBaseDao != null)//空了就代表建筑物升满级了
        {
            List<SpecialEventFailResult> failArray = new List<SpecialEventFailResult>();
            SpecialEventFailResult sefr = null;
            if (nextBaseDao.baseProDao.isSpecial != 1) //不是大升级，只需要检查钱够不够
            {
                if (CurrentUserData.user_gold < nextBaseDao.baseProDao.levelmoney) //钱不够
                {
                    //Debug.Log("钱不够");
                    sefr = new SpecialEventFailResult();
                    sefr.type = 3;
                    sefr.fail_result = "钱不够了";
                    failArray.Add(sefr);
                }
                else
                {
                    //Debug.Log("钱够了");
                }
            }
            else//大升级，检查多种条件 
            {
                for (int i = 0; i < nextBaseDao.special_list.Count; i++)
                {
                    sefr = new SpecialEventFailResult();
                    if (nextBaseDao.special_list[i].type == 1)//检查其他建筑物的等级 
                    {

                        int res_num = UserProductionunitDataTool.Instance.CheckCommunityProLevelAverage(nextBaseDao.special_list[i].comm_id, nextBaseDao.special_list[i].level, CurrentUserData.user_community_num);
                        if (res_num > 0)
                        {
                            //Debug.Log($"失败了，当前地图有建筑物没有满足升级条件，数量：{res_num}");
                            sefr.type = 1;
                            sefr.fail_result = $"失败了，当前地图有建筑物没有满足升级条件，数量：{res_num}";
                            failArray.Add(sefr);
                        }
                        else
                        {
                            //Debug.Log("其他建筑物 条件满足");
                        }
                    }
                    if (nextBaseDao.special_list[i].type == 2)//检查物业公司等级
                    {
                        if (CurrentUserData.user_level > nextBaseDao.special_list[i].comm_id)
                        {
                            //Debug.Log($"公司等级够了");
                        }
                        else
                        {
                            //Debug.Log("公司等级不够");
                            sefr.type = 2;
                            sefr.fail_result = "公司等级不够";
                            failArray.Add(sefr);
                        }
                    }
                }
                if (CurrentUserData.user_gold < nextBaseDao.baseProDao.levelmoney) //钱不够
                {
                    //Debug.Log("钱不够");
                    sefr = new SpecialEventFailResult();
                    sefr.type = 3;
                    sefr.fail_result = "钱不够了";
                    failArray.Add(sefr);
                }
                else
                {
                    //Debug.Log("钱够了");

                }
            }
            //最终检查
            if (failArray.Count > 0)
            {
                //Debug.Log($"建筑物{_buidId}不能升级");
                // 不符合条件，红点消失
                if (redDotDAO.ProductionRDObjList.Count > 0 && redDotDAO.ProductionRDObjList[_buidId - 1].activeSelf)
                {
                    redDotDAO.ProductionRDObjList[_buidId - 1].SetActive(false);
                }
                if (redDotDAO.MapbuildingList.Count > 0 && redDotDAO.MapbuildingList[_buidId - 1].activeSelf)
                {
                    redDotDAO.MapbuildingList[_buidId - 1].SetActive(false);
                }

                return failArray;
            }
            else
            {
                // 大地图建筑的箭头，还有mainui里的scroll那个cell的红点
                if (redDotDAO.ProductionRDObjList.Count > 0 && (_buidId - 1) < redDotDAO.ProductionRDObjList.Count)
                {
                    if (!redDotDAO.ProductionRDObjList[_buidId - 1].activeSelf) // 为了防止数组越界异常只能先这么做
                    {
                        redDotDAO.ProductionRDObjList[_buidId - 1].SetActive(true);
                    }
                }
                if (redDotDAO.MapbuildingList.Count > 0 && (_buidId - 1) < redDotDAO.MapbuildingList.Count)
                {
                    if (!redDotDAO.MapbuildingList[_buidId - 1].activeSelf)
                    {
                        redDotDAO.MapbuildingList[_buidId - 1].SetActive(true);
                    }
                }

                //Debug.Log($"建筑物{_buidId} 可以升级");
                return failArray;
            }
        }
        else
        {
            this.ShowPromotBox("当前建筑物以满级，请静待我们后续更新呦");
            return null;
        }
    }


    // 检查员工所需升级
    public IEnumerator CheckEmployeLevelup() 
    {
        List<UserEmployerDAO> _userEmployeList = UserEmployerDataTool.Instance.FindUserEmployeByCommid(CurrentUserData.user_community_num);
        for (int i=0;i<_userEmployeList.Count;i++) 
        {
            if (CurrentUserData.user_gold >= _userEmployeList[i].lvup_money) 
            {
                //Debug.Log($"当前员工:{_userEmployeList[i].employ_name}  --> 可以升级");
                if(!redDotDAO.YuangongBtn.activeSelf)
                    redDotDAO.YuangongBtn.SetActive(true);
            }
           
        }
        yield return null;
    }

    public double GetCurrentSec() {
        
        double res = (((int)System.Math.Floor(Global_GameDaysPassed) * OneDay_in_sec) + (int)Global_TimeSec);

        return res; 
    }

    public int GetCurrentGtMin() { return (int)Global_TimeMin; }

    public int GetCurrentGtHour(){ return (int)Global_GameHour; }

    public int GetCurrentGtDay(){ return (int)System.Math.Floor(Global_GameDaysPassed); }

    public int GetDailyTotalMin() 
    {
       
        int i = (int)Global_TimeMin + (GetCurrentGtHour() * 60);
        //int a = i - ((int)Global_GameDaysPassed * 12 * 60);
        return i;
    }
    SingleEmergencyDAO DisassembleEmergencyRoleId(string _emergency) // 突发事件的字符串拆解，居民ID：居民出生点位：突发事件对应剧情ID
    {
        string[] _str = _emergency.Split(':');
        SingleEmergencyDAO seDAO = new SingleEmergencyDAO();
        for (int i = 0; i < _str.Length; i++)
        {
            if (string.IsNullOrEmpty(_str[i]) || string.IsNullOrWhiteSpace(_str[i]))
                break;

            seDAO.emergency_role_id = _str[0];
            seDAO.emergency_role_spawn = _str[1];
            seDAO.emergency_id = _str[2];
        }
        return seDAO;
    }

    void OnApplicationPause(bool pause)
    {
        if (pause)
        {
            double daySec = this.GetCurrentSec();

            Debug.Log($"累计时间.秒：{this.GetCurrentSec() }");

            CurrentUserData.user_seconds = this.GetCurrentSec();
            UserProductionunitDataTool.Instance.UpdateALLUserProduction(_currentALLUserProdutionunitData);

            UserDataTool.Instance.UpdateUserData(CurrentUserData);
            SQLiteHelper.getInstance().CloseConnection();
        }
        else
        {
            //切换到前台时执行，游戏启动时执行一次
        }
    }

    // 大退时候存的数据
    void OnApplicationQuit()
    {
        double daySec = this.GetCurrentSec();

        Debug.Log($"累计时间.秒：{this.GetCurrentSec() }");

        CurrentUserData.user_seconds = this.GetCurrentSec();
        UserProductionunitDataTool.Instance.UpdateALLUserProduction(_currentALLUserProdutionunitData);

        UserDataTool.Instance.UpdateUserData(CurrentUserData);
        SQLiteHelper.getInstance().CloseConnection();
    }

   
}

public class GameTimeStruct 
{
    public int GameDay { set; get; }
    public int GameHours { set; get; }
    public int GameMinutes { set; get; }
}


public interface GameTimeChangeEventPublisher 
{
    event TimeChangeEvent timechangeEvent;
    void TimeChangeRaiseEvent(GameTimeState gameTimeState);
}
