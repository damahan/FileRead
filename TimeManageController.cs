using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public delegate void TimeChangeEvent(GameTimeState gameTimeState,double _curHour, double _curMin);
public delegate void BuildFixEvent(BuildingState buildState,int comm_id,int build_id);

public class TimeManageController : MonoBehaviour 
{
    #region ��������
    public double Global_TimeMin, Global_GameDay, Global_GameHour, Global_GameDaysPassed;
    public double Global_TimeSec;
    public float _TimeScale, _TimeScale_baifen;
    [Header("��Ϸʱ�����-->��λ����")]
    public int GTProportion = 15;
    public int OneDay_in_sec = 43200;//��Ϸ��һ��12Сʱ��43200��
    public bool testTouch = false;

    // ��Ϊ��������Ϸ��ʱ�䣬���ʱ����ͣ��ֱ����ͣ���棬�������������ʱ������ʱ�������֮��
    public User CurrentUserData;
    private double lastQueryHour = -1;
    private double lastQueryMin = -1;
    //������Ϸʱ���Ƿ���ͣ��ǩ
    private bool isPeause = true;
    private MainUIWnd mainuiWnd;
    private bool isNextDay = false;
    private GameObject CommObj;

    public float TimeSpeedup = 1.0f;//���ٱ���
    //��Ϸʱ����ͣ�Ĺ㲥
    public event TimeChangeEvent onTimeStateChange;
    public event BuildFixEvent onBuildFixEventChange;

    public List<UserProdutionunitDAO> _currentALLUserProdutionunitData;//��ǰ���ѡ���С��ȫ����������
    public List<UserReviewEventDAO> _DailyReviewList;
    public List<UserReviewEventDAO> _DailyEmergencyList;// ���ݲ���߼�������ͬ��ͻ������ڸĳ��ڴ��ͼ�������ģ�ͣ����Դ˴�������ͬ���ݽṹ
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

        _TimeScale = 43200 / (GTProportion * 60);//�뼶��λ
        _TimeScale_baifen = 12 * 60 * 0.01f;
        //��ʼ����ʱ��+6����Ϊ��Ϸ��һ����6��00-18:00
        Global_GameHour = (((Global_TimeSec / (60 * 60)) % 12) + 6);
        Global_GameDaysPassed = ((int)Global_TimeSec / OneDay_in_sec);
        if (Global_GameDaysPassed >= 1)
            Global_TimeSec = Global_TimeSec - (OneDay_in_sec*(int)Global_GameDaysPassed);

        // ������Ϸ��ʼ����ǰ��С��������״̬����ȡ��ǰС������������
        _currentALLUserProdutionunitData = UserProductionunitDataTool.Instance.FindALL();

        _DailyReviewList = UserReviewEventDataTool.Instance.FindAllReview();
        _DailyEmergencyList = UserReviewEventDataTool.Instance.FindAllEmergency();

        DontDestroyOnLoad(this);

    }

    //���ֶ���Ĳ�������startִ�У������д��ͻ��ִ��˳�������
    private void Start()
    {
        //��Ϸ��һ�����⴦���£���Ϊ�����¼����߼����ڵ������ʱ��Żᴥ�������Ե�һ��Ҫ�ս���Ϸ�ʹ���һ�ε�һ������ݲŻ���
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

            // ����Ϸʱ�䵽��18:00ʱ������Ϊ6:00�������¼�,�˴��߼���ÿСʱ��鲻һ�����˴��߼�����ʱ�����ã�������ÿ��17:59ʱ������
            if (Global_GameHour >= 17.99)
            {
                isNextDay = true;
                Global_TimeSec = 0;
                Global_GameDaysPassed++;  // ������Ϸ�ھ���������
                OnDayEnd(); // ����һ���·��������ڴ���ÿ�����ʱ���¼�
            }

            if (_tmeMin != lastQueryMin)
            {
                if (lastQueryMin > 0 && mainuiWnd!=null) // ����֮�������Ϸ�ᵼ�¹���ļ��أ��������start(),awake()��ûִ���꣬���Ե�һ֡�ȿչ�ȥ
                {
                    mainuiWnd.RefreshClock(_tmeMin,_tmpHour);
                }
                lastQueryMin = _tmeMin;
            }

            // ÿ5�����ж�һ��
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

    // һ������ļ���¼�
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
        //ÿ�����ʱ�̣���ѯ��һ�����������Global_GameDaysPassed�ڴ�ʱ�Ѿ�++�����һ��
        BaseEventCategoryDAO b = BaseEventCategoryDataTool.Instance.FindByGameDay(cur_chatper_days, CurrentUserData.unlock_new_community_num);

        //������յ�ͼ��û������ͻ���¼�
        for (int i=0;i< CommObj.GetComponent<Community1Controller>().emergencyRoleList.Count;i++) 
        {
            if (CommObj.GetComponent<Community1Controller>().emergencyRoleList[i] != null) 
            {
                UserReviewEventDataTool.Instance.UpdateUserReview(CommObj.GetComponent<Community1Controller>().emergencyRoleList[i].GetComponent<CitizenRoleHandler>().GetEventId()); // ��Ҫ�ȸ���user_review���յ�ͻ���¼�����
                Destroy(CommObj.GetComponent<Community1Controller>().emergencyRoleList[i]);
            }
        }

        // ÿ�����ʱ����������¼���
        UpdateDailyFixEvent(b);
        UpdateDailyReviewEvent(b);
        UpdateDailyEmergency(b);
    }

    // ����ÿ��������¼�
    void UpdateDailyFixEvent(BaseEventCategoryDAO becDAO) 
    {
        if (becDAO != null)
        {
            #region �����������
            string[] fixTaskArray = DisassembleEventTask(becDAO.fix_event_id);
            List<GameTimeStruct> CurrentDayilyFixEventList = DisassembleEventTime(becDAO.fix_event_time);
            for (int j = 0; j < CurrentDayilyFixEventList.Count; j++)
            {
                SingleFixEventDAO _single = new SingleFixEventDAO();
                if (becDAO.fix_random != 0) // �������ȡ
                {
                    if (fixTaskArray[j].Equals(""))
                        break;
                    _single.fix_event_id = fixTaskArray[j];

                }
                else// �����ȡ�¼� 
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
                // �������µ�ͼÿ���������������ά���¼���ֵ���������������ϣ�ͬһ��������һ����ܻ��ж��ά���¼�����
                for (int k=0; k < _currentALLUserProdutionunitData.Count; k++) 
                {
                    if (bflDAO.community_id == _currentALLUserProdutionunitData[k].community_num && 
                        _currentALLUserProdutionunitData[k].commmunity_build_num == bflDAO.build_id && 
                        _currentALLUserProdutionunitData[k].buildingSpecialEventDAO.buildingState == BuildingState.BuildingRunning) 
                    {
                        //�˴���Ҫ��ֵ����Ϊ���û��п��ܵ����˴���ʱ��֮�󣬽�����״̬����������û���û�н�������ʹ�����Ϸ��������Ҫ�洢�����û��ٽ���Ϸ����Ҫ�ô�event_idȥ���Ҷ�Ӧ�¼�����
                        _currentALLUserProdutionunitData[k].buildingSpecialEventDAO.eventId =int.Parse(_single.fix_event_id);

                        BuildingSingleFixEventDAO bsfeDAO = new BuildingSingleFixEventDAO();
                        bsfeDAO.build_id = bflDAO.build_id;
                        bsfeDAO.community_id = bflDAO.community_id;
                        bsfeDAO.fix_time = bflDAO.fix_time;
                        bsfeDAO.triger_time = _single.triger_time;
                        bsfeDAO.bonus_type = bflDAO.bonus_type;
                        bsfeDAO.alert_string = bflDAO.alert_string;
                        _currentALLUserProdutionunitData[k].singleFixEventDAO =bsfeDAO;
                        Debug.Log($"������:{_currentALLUserProdutionunitData[k].singleFixEventDAO.build_id}   " +
                            $"�����¼�{_currentALLUserProdutionunitData[k].singleFixEventDAO.build_id }  " +
                            $"Ԥ�ƴ���ʱ���룺{_currentALLUserProdutionunitData[k].singleFixEventDAO.triger_time} ���Ӻ�����   " +
                            $"�������¼�����ʱ�䣺{_currentALLUserProdutionunitData[k].singleFixEventDAO.fix_time}");
                    }
                }
            }
            #endregion
        }
        else
        {
            Debug.Log($"û������");
        }
    }
    // ����ÿ��ͻ���¼�
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
                    ureDAO.review_or_emergency = 2; // �����ݽṹ��ͬ�����Կ��˱�ǩ�����жϣ���ǩ���������UserReviewEventDAO
                    ureDAO.review_event_id = int.Parse(_single.emergency_id);
                    ureDAO.review_event_time = _single.triger_time;
                    ureDAO.event_name = _single.emergency_role_spawn;
                    ureDAO.event_context = _single.emergency_role_id;
                    ureDAO.is_stop_time = 0;// ͻ���¼���Զ��ֹͣʱ��
                    ureDAO.comm_id = 1; //  ��Ϊ�Ѿ����˾���ģ��id���Դ˴���������С��id

                    //��ʱӦ�ò�������
                    UserReviewEventDataTool.Instance.AddUserReview(ureDAO);
                    _DailyEmergencyList.Add(ureDAO);
                    Debug.Log($"ͻ���¼�:{i}�� --> ����ʱ��:{_DailyEmergencyList[i].review_event_time}�� --> �¼�����:{_DailyEmergencyList[i].event_name}");
                }
            }
            else
            {
                Debug.Log("����û��ͻ���¼�");
            }
        }
    }

    // ����ÿ�������¼�
    void UpdateDailyReviewEvent(BaseEventCategoryDAO becDAO) 
    {
        #region ���������� ��Ҫ���user_review����������ݾͲ���Ҫ��飬ֱ�Ӵ�user_review��ȡ����
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

                //��ʱӦ�ò�������
                UserReviewEventDataTool.Instance.AddUserReview(ureDAO);
                _DailyReviewList.Add(ureDAO);
                Debug.Log($"����¼�:{i}�� --> ����ʱ��:{_DailyReviewList[i].review_event_time}�� --> �¼�����:{_DailyReviewList[i].event_name}");
            }
        }
        #endregion
    }

    // ��������id�����ݲ�֣����ݸ�ʽ��;Ϊ�ָ������������һ������ҲҪ���Ϸֺ�
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
            QuestQueryResult qqr = MainQuestTotalDataTool.Instance.IncrementCompleteNum((int)MainQuestEnum.�ۼƻ��X���,_gold);
            mainuiWnd.RefreshMainQuest((int)MainQuestEnum.�ۼƻ��X���, qqr.res_count);
        }
    }

    public void GoldChangeDecrease(int _gold) 
    {
        if (MainQuestTotalDataTool.Instance != null && mainuiWnd != null) 
        {
            //Debug.Log($"time.gold.decr:{_gold}");
            QuestQueryResult qqr = MainQuestTotalDataTool.Instance.IncrementCompleteNum((int)MainQuestEnum.�ۼ�����X���,_gold);
            mainuiWnd.RefreshMainQuest((int)MainQuestEnum.�ۼ�����X���, qqr.res_count);
        }
    }

    // ����ʱ������ݲ���
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


    //��Ϸ��ÿ���Ӽ������¼�
    IEnumerator TimeCheckOnMin() 

    {   // ÿ���Ӽ�齨���������¼�״̬��
        if (_currentALLUserProdutionunitData.Count > 0)
        {
            for (int i = 0; i < _currentALLUserProdutionunitData.Count; i++)
            {
               
                //���ȼ����������ﵱǰ�¼�״̬��ά�޺���������Ϊ����,������������У������Դ���ά��״̬
                if (_currentALLUserProdutionunitData[i].buildingSpecialEventDAO.buildingState == BuildingState.BuildingRunning) 
                {
                   
                    if (_currentALLUserProdutionunitData[i].singleFixEventDAO.triger_time >0 &&
                        _currentALLUserProdutionunitData[i].singleFixEventDAO!=null && (GetDailyTotalMin()) >= _currentALLUserProdutionunitData[i].singleFixEventDAO.triger_time) 
                    {
                        _currentALLUserProdutionunitData[i].buildingSpecialEventDAO.buildingState = BuildingState.BuildingBreak;

                        onBuildFixEventChange(_currentALLUserProdutionunitData[i].buildingSpecialEventDAO.buildingState, _currentALLUserProdutionunitData[i].community_num, _currentALLUserProdutionunitData[i].commmunity_build_num);
                        Debug.Log($"������{_currentALLUserProdutionunitData[i].commmunity_build_num} ���ˣ���   ��ǰ�ۼƷ�����:{(GetDailyTotalMin())}    Ԥ�ƴ���ʱ��:{_currentALLUserProdutionunitData[i].singleFixEventDAO.triger_time}");
                    }
                }
                //����ý�����һֱû���������ҵ��������˸Ľ��������¼�
                //if (_currentALLUserProdutionunitData[i].buildingSpecialEventDAO.buildingState == BuildingState.BuildingBreak)
                //{
                //}

                //���������������У������������¼�������ɾ���������¼�
                if (_currentALLUserProdutionunitData[i].buildingSpecialEventDAO.buildingState == BuildingState.BuildingSpecialUpdating)
                {
                    if (GetCurrentSec() >= _currentALLUserProdutionunitData[i].buildingSpecialEventDAO.EndingTime)
                    {
                        //TODO:��Ҫ�������������GameObjһ��֪ͨ��������Щ����Ҫ��ϸ �ݶ� ���ͼ������Ч��UI��Ļ����toast��ʾ
                        //Debug.Log($"-------------------------��{BuildSpecialList[i]}�Ž���������������!");
                    }
                    if (_currentALLUserProdutionunitData[i].singleFixEventDAO !=null && (GetDailyTotalMin()) >= _currentALLUserProdutionunitData[i].singleFixEventDAO.triger_time) 
                    {
                        // ���������¼�������ɾ�����¼�
                        //_currentALLUserProdutionunitData[i].singleFixEventDAO = null;
                    }
                }
            }
        }
        // ÿ���Ӽ���������
        if (_DailyReviewList!=null && _DailyReviewList.Count > 0) 
        {
            for (int i=0;i<_DailyReviewList.Count;i++) 
            {
                if (GetDailyTotalMin() >= _DailyReviewList[i].review_event_time) 
                {
                    Debug.Log($"�����{i}�� �������:{_DailyReviewList[i].event_name}");
                    mainuiWnd.OnReviewEventActive(_DailyReviewList[i]);
                    _DailyReviewList.RemoveAt(i);
                }
            }
        }
        // ÿ���Ӽ��ͻ������
        if (_DailyEmergencyList != null && _DailyEmergencyList.Count > 0)
        {
            for (int i = 0; i < _DailyEmergencyList.Count; i++)
            {
                if (GetDailyTotalMin() >= _DailyEmergencyList[i].review_event_time)
                {
                    Debug.Log($"�����{i}�� ͻ������:{_DailyEmergencyList[i].event_name}");

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


    //��ͣ��Ϸʱ��
    public void GameTimePeause() 
    {
        isPeause = false;
        onTimeStateChange(GameTimeState.GameTimePeause, Global_GameHour, Global_TimeMin);
    }

    //�ָ���Ϸʱ��
    public void GameTimeReStart() 
    {
        isPeause = true;
        onTimeStateChange(GameTimeState.GameTimeRunning, Global_GameHour, Global_TimeMin);
    }

    // ������ҵ��˾�ľ���
    public void UserEXPUpdate(int exp) 
    {
        CurrentUserData.user_exp += exp;
        //CurrentUserData.user_seconds = 
        mainuiWnd.RefreshLvAndEXP();

        BaseCompanyMoreInfoDAO bcmi = BaseCompanyDataTool.Instance.FindNextLevel(CurrentUserData.user_level);
        if (CurrentUserData.user_exp >= bcmi.baseCompanyDAO.level_exp) {
            mainuiWnd.OnCompanyLevelupBtnClick();
            mainuiWnd.RefreshLvAndEXP();
            Debug.Log("��һ�����辭���Ѿ���λ");
        }
    }

    public void UpdateUserCitizen(int _favor,int _cid) 
    {
        //����µ�ǰ�������ĺøж��Ƿ������ȡ��ҵ��˾������
        UserCitizenDAO ucDAO = UserCitizenDataTool.Instance.FindByCId(_cid);
        BaseCitizenDAO bcDAO = BaseCitizenDataTool.Instance.FindById(_cid);

        int _res = _favor + ucDAO.current_favorability;
        UserCitizenDataTool.Instance.UpdateCurrentFavorability(_res, _cid);

        QuestQueryResult qqr = MainQuestTotalDataTool.Instance.IncrementCompleteNum((int)MainQuestEnum.X��NPC�øжȴﵽX);
        mainuiWnd.RefreshMainQuest((int)MainQuestEnum.X��NPC�øжȴﵽX, qqr.res_count);

        //List<string> citizenFavorList = BaseCitizenDataTool.Instance.ParseCompanyExForFavorability(bcDAO.companyex_for_favorability);
        //for (int i=0;i < citizenFavorList.Count;i++) 
        //{
        //    if (ucDAO.current_favorability >= int.Parse(citizenFavorList[i])) 
        //    {
        //        //֤����ǰ�øжȿ�����ȡ���������е�ĳһ������Ҫȥ�Ҷ�Ӧ���Ƿ���ȡ����û��ȥ����ȥ���������
        //        if (i == 0 && ucDAO.current_companyex_1 == 0)
        //            Debug.Log($"��{_cid} �ž���ĵ�һ������������ȡ��");
        //        if (i == 1 && ucDAO.current_companyex_1 == 0)
        //            Debug.Log($"��{_cid} �ž���ĵڶ�������������ȡ��");
        //    }
        //}
    }

    public IEnumerator CheckCitizenLevel() 
    {
        List<UserCitizenDAO> allUCList = UserCitizenDataTool.Instance.FindAllCitizens();
        List<BaseCitizenDAO> allBCList = BaseCitizenDataTool.Instance.FindAllCitizens();

        bool isTotalRedDot = false;// mainui�Ǹ���ť�ĺ���ж�
        bool isTotalPointDot = false;// mainui�Ǹ������������»�����������ͷ����Ǹ�����ж�
        
        for (int i=0;i<allUCList.Count;i++) 
        {
            List<string> _cFavorList = BaseCitizenDataTool.Instance.ParseCompanyExForFavorability(allBCList[i].companyex_for_favorability);
            for (int j=0;j<_cFavorList.Count;j++) 
            {
                if (allUCList[i].current_favorability >= int.Parse(_cFavorList[j]))
                {
                    if (j == 0 && allUCList[i].current_companyex_1 == 0)
                    {
                        //Debug.Log($"��{allUCList[i].cid} �ž��� {allBCList[i].name} �ĵ�һ������������ȡ��");
                         isTotalPointDot = true;
                         isTotalRedDot = true;
                    }

                    if (allUCList[i].current_companyex_1 == 1  ) // ��Һøж�û�ﵽ2����ȡ�������Ѿ���ȡ��1�ţ��Ͳ��õ�
                    {
                        isTotalPointDot = false;
                    }

                    if (j == 1 && allUCList[i].current_companyex_2 == 0)
                    {
                       // Debug.Log($"��{allUCList[i].cid} �ž��� {allBCList[i].name} �ĵڶ�������������ȡ��");
                        isTotalPointDot = true;
                        isTotalRedDot = true;
                    }

                    if (allUCList[i].current_companyex_1 == 1 && allUCList[i].current_companyex_2 == 1  ) // �����ж���Ϊ��Ԥ�����ֻ����2��������û��1������������Ҫ�����
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
        
        if (!isTotalRedDot) // �˴�֤��ȷʵû�п�����ʾ����
        {
            redDotDAO.JuminBtn.SetActive(false);
        }
        if (isTotalRedDot) 
        {
            redDotDAO.JuminBtn.SetActive(true);
        }
        yield return null;
    }

    //���еĺ����ͳһ����Ϊreddot���Կ���ֱ�ӷ�װ��
    private GameObject FindRedDot(GameObject _parentObj) 
    {
        return _parentObj.transform.Find("reddot").gameObject;
    }

    // ���ȫ���������Ƿ���Ҫ��������ʾ�����,����������������ť���Ǹ������İ�ť
    public IEnumerator CheckALLBuildNeedLevelup()
    {
        List<UserProdutionunitDAO> listUP = UserProductionunitDataTool.Instance.FindByCommId(CurrentUserData.user_community_num);
        bool totalHasLvUp = false;
        for (int i = 0; i < listUP.Count; i++)
        {
            //Debug.Log($"-------------------------------->��ǰ��齨�����{listUP[i].commmunity_build_num}��<------------------------------------------");

            bool singleLvUp = CheckBuildLevelup(listUP[i].commmunity_build_num);
            if (singleLvUp) 
            {
                totalHasLvUp = true;
                break; //ֻҪ��һ�����˾��У�����ζ�Ű�ť��������ʾ
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

    // ��鵥���������Ƿ���Ҫ����
    public bool CheckBuildLevelup(int _buidId)
    {
        int curCommunity = CurrentUserData.user_community_num;
        UserProdutionunitDAO updao = UserProductionunitDataTool.Instance.FindProductionunityByNumandCom(_buidId, curCommunity);
        BaseProdutionMoreInfoDAO nextBaseDao = BaseProdutionunitDataTool.Instance.FindProductionNextLevel(curCommunity, _buidId, updao.level);

        if (nextBaseDao != null)//���˾ʹ���������������
        {
            bool isMoneyEnough = CurrentUserData.user_gold >= nextBaseDao.baseProDao.levelmoney;
            bool areOtherBuildingsReady = true;
            bool isCompanyLevelEnough = true;

            if (nextBaseDao.baseProDao.isSpecial == 1) //������������������ 
            {
                for (int i = 0; i < nextBaseDao.special_list.Count; i++)
                {
                    if (nextBaseDao.special_list[i].type == 1)//�������������ĵȼ� 
                    {
                        int res_num = UserProductionunitDataTool.Instance.CheckCommunityProLevelAverage(nextBaseDao.special_list[i].comm_id, nextBaseDao.special_list[i].level, CurrentUserData.user_community_num);
                        if (res_num > 0)
                        {
                            areOtherBuildingsReady = false;
                            break; // һ���ҵ������������Ľ������˳�ѭ��
                        }
                    }
                    if (nextBaseDao.special_list[i].type == 2)//�����ҵ��˾�ȼ�
                    {
                        if (CurrentUserData.user_level <= nextBaseDao.special_list[i].comm_id)
                        {
                            isCompanyLevelEnough = false;
                            break; // һ����˾�ȼ����㣬�˳�ѭ��
                        }
                    }
                }
            }

            if ((nextBaseDao.baseProDao.isSpecial != 1 && isMoneyEnough) ||
            (nextBaseDao.baseProDao.isSpecial == 1 && isMoneyEnough && areOtherBuildingsReady && isCompanyLevelEnough))
            {
                // ���ͼ�����ļ�ͷ������mainui���scroll�Ǹ�cell�ĺ��
                if (redDotDAO.ProductionRDObjList.Count > 0 && (_buidId - 1) < redDotDAO.ProductionRDObjList.Count)
                {
                    if (!redDotDAO.ProductionRDObjList[_buidId - 1].activeSelf) // Ϊ�˷�ֹ����Խ���쳣ֻ������ô��
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
                // ���غ��
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
            this.ShowPromotBox("��ǰ���������������뾲�����Ǻ���������");
            return false;
        }
    }


    // ��鵥���������Ƿ���Ҫ����
    public List<SpecialEventFailResult> CheckBuildLevelupForBuildBtn(int _buidId)
    {
        int curCommunity = CurrentUserData.user_community_num;
        UserProdutionunitDAO updao = UserProductionunitDataTool.Instance.FindProductionunityByNumandCom(_buidId, curCommunity);
        BaseProdutionMoreInfoDAO nextBaseDao = BaseProdutionunitDataTool.Instance.FindProductionNextLevel(curCommunity, _buidId, updao.level);
        //Debug.Log($"CheckBuildLevelup.updao:{CurrentUserData.user_gold}");
        if (nextBaseDao != null)//���˾ʹ���������������
        {
            List<SpecialEventFailResult> failArray = new List<SpecialEventFailResult>();
            SpecialEventFailResult sefr = null;
            if (nextBaseDao.baseProDao.isSpecial != 1) //���Ǵ�������ֻ��Ҫ���Ǯ������
            {
                if (CurrentUserData.user_gold < nextBaseDao.baseProDao.levelmoney) //Ǯ����
                {
                    //Debug.Log("Ǯ����");
                    sefr = new SpecialEventFailResult();
                    sefr.type = 3;
                    sefr.fail_result = "Ǯ������";
                    failArray.Add(sefr);
                }
                else
                {
                    //Debug.Log("Ǯ����");
                }
            }
            else//������������������ 
            {
                for (int i = 0; i < nextBaseDao.special_list.Count; i++)
                {
                    sefr = new SpecialEventFailResult();
                    if (nextBaseDao.special_list[i].type == 1)//�������������ĵȼ� 
                    {

                        int res_num = UserProductionunitDataTool.Instance.CheckCommunityProLevelAverage(nextBaseDao.special_list[i].comm_id, nextBaseDao.special_list[i].level, CurrentUserData.user_community_num);
                        if (res_num > 0)
                        {
                            //Debug.Log($"ʧ���ˣ���ǰ��ͼ�н�����û����������������������{res_num}");
                            sefr.type = 1;
                            sefr.fail_result = $"ʧ���ˣ���ǰ��ͼ�н�����û����������������������{res_num}";
                            failArray.Add(sefr);
                        }
                        else
                        {
                            //Debug.Log("���������� ��������");
                        }
                    }
                    if (nextBaseDao.special_list[i].type == 2)//�����ҵ��˾�ȼ�
                    {
                        if (CurrentUserData.user_level > nextBaseDao.special_list[i].comm_id)
                        {
                            //Debug.Log($"��˾�ȼ�����");
                        }
                        else
                        {
                            //Debug.Log("��˾�ȼ�����");
                            sefr.type = 2;
                            sefr.fail_result = "��˾�ȼ�����";
                            failArray.Add(sefr);
                        }
                    }
                }
                if (CurrentUserData.user_gold < nextBaseDao.baseProDao.levelmoney) //Ǯ����
                {
                    //Debug.Log("Ǯ����");
                    sefr = new SpecialEventFailResult();
                    sefr.type = 3;
                    sefr.fail_result = "Ǯ������";
                    failArray.Add(sefr);
                }
                else
                {
                    //Debug.Log("Ǯ����");

                }
            }
            //���ռ��
            if (failArray.Count > 0)
            {
                //Debug.Log($"������{_buidId}��������");
                // �����������������ʧ
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
                // ���ͼ�����ļ�ͷ������mainui���scroll�Ǹ�cell�ĺ��
                if (redDotDAO.ProductionRDObjList.Count > 0 && (_buidId - 1) < redDotDAO.ProductionRDObjList.Count)
                {
                    if (!redDotDAO.ProductionRDObjList[_buidId - 1].activeSelf) // Ϊ�˷�ֹ����Խ���쳣ֻ������ô��
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

                //Debug.Log($"������{_buidId} ��������");
                return failArray;
            }
        }
        else
        {
            this.ShowPromotBox("��ǰ���������������뾲�����Ǻ���������");
            return null;
        }
    }


    // ���Ա����������
    public IEnumerator CheckEmployeLevelup() 
    {
        List<UserEmployerDAO> _userEmployeList = UserEmployerDataTool.Instance.FindUserEmployeByCommid(CurrentUserData.user_community_num);
        for (int i=0;i<_userEmployeList.Count;i++) 
        {
            if (CurrentUserData.user_gold >= _userEmployeList[i].lvup_money) 
            {
                //Debug.Log($"��ǰԱ��:{_userEmployeList[i].employ_name}  --> ��������");
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
    SingleEmergencyDAO DisassembleEmergencyRoleId(string _emergency) // ͻ���¼����ַ�����⣬����ID�����������λ��ͻ���¼���Ӧ����ID
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

            Debug.Log($"�ۼ�ʱ��.�룺{this.GetCurrentSec() }");

            CurrentUserData.user_seconds = this.GetCurrentSec();
            UserProductionunitDataTool.Instance.UpdateALLUserProduction(_currentALLUserProdutionunitData);

            UserDataTool.Instance.UpdateUserData(CurrentUserData);
            SQLiteHelper.getInstance().CloseConnection();
        }
        else
        {
            //�л���ǰ̨ʱִ�У���Ϸ����ʱִ��һ��
        }
    }

    // ����ʱ��������
    void OnApplicationQuit()
    {
        double daySec = this.GetCurrentSec();

        Debug.Log($"�ۼ�ʱ��.�룺{this.GetCurrentSec() }");

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
